#!/usr/bin/env python3
"""
buffers_gen_c.py - Parse wire structs from a C header and generate
                   read/write functions for each struct.

Usage: python buffers_gen_c.py [--fixed] [--big-endian] [--prefix <pfx>] [--buffers] [--out <dir>] <header.h>

Options:
  --fixed           Use fixed-size serialization for arrays and strings
                   (transmit entire declared size). Without this flag,
                   arrays/strings are length-prefixed on the wire.
  --big-endian     Generate struct read/write functions using big-endian
                   serialization. Without this flag, little-endian is used.
  --prefix <pfx>   Prepend <pfx> to every generated function name and
                   per-struct #define (not to the MAX_SIZE define).
  --buffers        Also emit buffers.h / buffers.c support files.
  --out <dir>      Directory for generated files (default: same as input).

Outputs:
  <stem>_buffers.h   - declarations for all read/write functions
  <stem>_buffers.c   - implementations

Function naming:
  - Typedef names ending in _t have _t stripped
  - struct name precedes _read / _write (no LE/BE suffix)
  - e.g. example_data_message_t -> [prefix]example_data_message_read / [prefix]example_data_message_write
  - The shared buffers.h/.c functions use _le and _be suffixes
"""

import os
import re
import sys

# ---------------------------------------------------------------------------
# Type mapping
# ---------------------------------------------------------------------------

SCALAR_TYPE_MAP = {
    'uint8_t':            'uint8_t',
    'uint16_t':           'uint16_t',
    'uint32_t':           'uint32_t',
    'uint64_t':           'uint64_t',
    'int8_t':             'int8_t',
    'int16_t':            'int16_t',
    'int32_t':            'int32_t',
    'int64_t':            'int64_t',
    'bool':               'uint8_t',
    'char':               'int8_t',
    'unsigned char':      'uint8_t',
    'short':              'int16_t',
    'unsigned short':     'uint16_t',
    'int':                'int32_t',
    'unsigned int':       'uint32_t',
    'long':               'int32_t',
    'unsigned long':      'uint32_t',
    'long long':          'int64_t',
    'unsigned long long': 'uint64_t',
    'float':              'float',
    'double':             'double',
    'wchar_t':            'int16_t',
    'size_t':             'uint32_t',
}

WIRE_SCALAR_TYPES = {
    'uint8_t', 'uint16_t', 'uint32_t', 'uint64_t',
    'int8_t',  'int16_t',  'int32_t',  'int64_t',
    'float',   'double',
}

# Single-byte wire types have no byte order - no _le/_be suffix needed
SINGLE_BYTE_WIRE_TYPES = {'uint8_t', 'int8_t'}

# C types that map to single-byte wire types (aliases)
SINGLE_BYTE_C_TYPES = {'char', 'unsigned char', 'bool'}

WIRE_TYPE_SIZES = {
    'uint8_t':  1,
    'uint16_t': 2,
    'uint32_t': 4,
    'uint64_t': 8,
    'int8_t':   1,
    'int16_t':  2,
    'int32_t':  4,
    'int64_t':  8,
    'float':    4,
    'double':   8,
}

def enum_wire_type(min_val: int, max_val: int) -> str:
    if min_val >= 0:
        if max_val <= 0xFF:           return 'uint8_t'
        elif max_val <= 0xFFFF:       return 'uint16_t'
        elif max_val <= 0xFFFFFFFF:   return 'uint32_t'
        else:                         return 'uint64_t'
    else:
        if   min_val >= -128         and max_val <= 127:         return 'int8_t'
        elif min_val >= -32768       and max_val <= 32767:       return 'int16_t'
        elif min_val >= -2147483648  and max_val <= 2147483647:  return 'int32_t'
        else:                                                    return 'int64_t'


def length_prefix_type(array_len: int) -> str:
    """Return the wire type for the length prefix based on array capacity."""
    if array_len < 256:
        return 'uint8_t'
    elif array_len < 65536:
        return 'uint16_t'
    elif array_len <= 0xFFFFFFFF:
        return 'uint32_t'
    else:
        error(f"Array length {array_len} exceeds UINT32_MAX")


def length_prefix_size(array_len: int) -> int:
    """Return the byte size of the length prefix for a given array capacity."""
    return WIRE_TYPE_SIZES[length_prefix_type(array_len)]


def endian_suffix_for_type(c_type: str, endian_suffix: str) -> str:
    """Return the appropriate endian suffix for a buffers_read/write call.

    Single-byte types (uint8_t, int8_t, char, unsigned char, bool) have no
    byte order, so they use no suffix.  All multi-byte types use the given
    endian_suffix (_le or _be).
    """
    wire = SCALAR_TYPE_MAP.get(c_type, c_type)
    if wire in SINGLE_BYTE_WIRE_TYPES:
        return ""
    return endian_suffix


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def error(msg: str) -> None:
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(1)


def strip_comments(text: str) -> str:
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.DOTALL)
    text = re.sub(r'//[^\n]*', '', text)
    return text


def strip_preprocessor(text: str) -> str:
    text = re.sub(r'^\s*#[^\n]*(?:\\\n[^\n]*)*', '', text, flags=re.MULTILINE)
    return text


def type_fn_suffix(type_name: str) -> str:
    """Strip trailing _t from typedef names for use in function names."""
    if type_name.endswith('_t'):
        return type_name[:-2]
    return type_name

def header_stem_to_define_prefix(header_path: str) -> str:
    """Convert header filename stem to a valid uppercase C identifier prefix."""
    stem = os.path.splitext(os.path.basename(header_path))[0]
    ident = re.sub(r'[^A-Za-z0-9]', '_', stem).upper()
    if ident and ident[0].isdigit():
        ident = '_' + ident
    return ident


def struct_size_define_name(struct_name: str, user_prefix: str = "") -> str:
    """Return the #define name for a struct's wire size.

    e.g. nop_message_t  -> NOP_MESSAGE_SIZE
         with user_prefix="EX_" -> EX_NOP_MESSAGE_SIZE
    """
    name = struct_name
    if name.endswith('_t'):
        name = name[:-2]
    name = re.sub(r'[^A-Za-z0-9]', '_', name).upper()
    up = user_prefix.upper()
    return f"{up}{name}_SIZE"

# ---------------------------------------------------------------------------
# Enum parsing
# ---------------------------------------------------------------------------

ENUM_TYPEDEF_RE = re.compile(
    r'\btypedef\s+enum\s*(?:[A-Za-z_][A-Za-z0-9_]*)?\s*'
    r'\{(?P<body>[^{}]*)\}\s*(?P<n>[A-Za-z_][A-Za-z0-9_]*)\s*;',
    re.DOTALL,
)
ENUM_RE = re.compile(
    r'\benum\s+(?P<n>[A-Za-z_][A-Za-z0-9_]*)\s*\{(?P<body>[^{}]*)\}\s*;',
    re.DOTALL,
)
_INT_LITERAL_RE = re.compile(r'^-?\s*(?:0[xX][0-9A-Fa-f]+|0[0-7]*|[1-9][0-9]*|0)$')


def parse_enums(text: str) -> dict:
    enums = {}
    found = []
    for m in ENUM_TYPEDEF_RE.finditer(text):
        found.append((m.group('n'), m.group('body')))
    typedef_names = {n for n, _ in found}
    for m in ENUM_RE.finditer(text):
        if m.group('n') not in typedef_names:
            found.append((m.group('n'), m.group('body')))
    for name, body in found:
        if name in enums:
            error(f"Duplicate enum name: '{name}'")
        values = []
        current = 0
        for entry in body.split(','):
            entry = entry.strip()
            if not entry:
                continue
            if '=' in entry:
                lhs, rhs = entry.split('=', 1)
                rhs = rhs.strip()
                if not _INT_LITERAL_RE.fullmatch(rhs):
                    error(f"Enum '{name}': non-literal value '{rhs}' not supported")
                current = int(rhs, 0)
            values.append(current)
            current += 1
        if values:
            enums[name] = enum_wire_type(min(values), max(values))
    return enums


# ---------------------------------------------------------------------------
# Field parsing
# ---------------------------------------------------------------------------

FIELD_RE = re.compile(
    r'^\s*'
    r'(?P<type>[A-Za-z_][A-Za-z0-9_ ]*?)'
    r'\s+'
    r'(?P<n>[A-Za-z_][A-Za-z0-9_]*)'
    r'(?:\s*\[\s*(?P<len>[^\]]+)\s*\])?'
    r'\s*;',
    re.MULTILINE,
)

def resolve_type(type_str, struct_name, field_name, all_struct_names, known_enums):
    key = ' '.join(type_str.lower().split())
    if key in SCALAR_TYPE_MAP:
        return SCALAR_TYPE_MAP[key]
    if type_str in known_enums:
        return known_enums[type_str]
    if type_str in all_struct_names:
        return type_str
    error(f"Struct '{struct_name}', field '{field_name}': unsupported or unknown type '{type_str}'")


def parse_field(raw, struct_name, all_struct_names, known_enums):
    raw = raw.strip()
    if not raw:
        return None
    if '*' in raw:
        error(f"Struct '{struct_name}': pointer field not allowed: '{raw}'")
    m = FIELD_RE.match(raw + (';' if not raw.endswith(';') else ''))
    if not m:
        error(f"Struct '{struct_name}': cannot parse field: '{raw}'")
    type_str = ' '.join(m.group('type').split())
    name = m.group('n')
    len_str = m.group('len')
    array_len = None
    if len_str is not None:
        len_str = len_str.strip()
        if not re.fullmatch(r'\d+', len_str):
            error(f"Struct '{struct_name}', field '{name}': array length must be a literal integer, got '{len_str}'")
        array_len = int(len_str)
    wire_type = resolve_type(type_str, struct_name, name, all_struct_names, known_enums)
    is_enum = False
    if type_str in known_enums:
        is_enum = True
    return {"name": name, "type": type_str, "wire_type": wire_type, "array_len": array_len, "is_enum": is_enum}


def parse_struct_body(body, struct_name, all_struct_names, known_enums):
    fields = []
    for raw in body.split(';'):
        raw = raw.strip()
        if not raw:
            continue
        if re.search(r'\b(struct|union)\b', raw):
            error(f"Struct '{struct_name}': nested struct/union not allowed: '{raw}'")
        field = parse_field(raw + ';', struct_name, all_struct_names, known_enums)
        if field:
            fields.append(field)
    return fields


# ---------------------------------------------------------------------------
# Top-level struct extraction
# ---------------------------------------------------------------------------

STRUCT_RE = re.compile(
    r'\bstruct\s+(?P<n>[A-Za-z_][A-Za-z0-9_]*)\s*\{(?P<body>[^{}]*)\}\s*;',
    re.DOTALL,
)
TYPEDEF_RE = re.compile(
    r'\btypedef\s+struct\s*(?:[A-Za-z_][A-Za-z0-9_]*)?\s*\{(?P<body>[^{}]*)\}\s*(?P<n>[A-Za-z_][A-Za-z0-9_]*)\s*;',
    re.DOTALL,
)


def parse_header(text: str) -> dict:
    text = strip_comments(text)
    text = strip_preprocessor(text)

    known_enums = parse_enums(text)

    found = []
    for m in TYPEDEF_RE.finditer(text):
        found.append((m.start(), m.group('n'), m.group('body')))
    typedef_spans = [(m.start(), m.end()) for m in TYPEDEF_RE.finditer(text)]
    for m in STRUCT_RE.finditer(text):
        if not any(ts <= m.start() and m.end() <= te for ts, te in typedef_spans):
            found.append((m.start(), m.group('n'), m.group('body')))
    found.sort(key=lambda x: x[0])

    all_struct_names = {name for _, name, _ in found}
    structs = {}
    for _, name, body in found:
        if name in structs:
            error(f"Duplicate struct name: '{name}'")
        fields = parse_struct_body(body, name, all_struct_names, known_enums)
        seen = set()
        for f in fields:
            if f['name'] in seen:
                error(f"Struct '{name}': duplicate field name '{f['name']}'")
            seen.add(f['name'])
        structs[name] = {"fields": fields}

    return structs

# ---------------------------------------------------------------------------
# Wire size computation
# ---------------------------------------------------------------------------

def wire_size_of(wire_type: str, array_len, structs: dict,
                 _visiting: frozenset = frozenset(), fixed_mode: bool = True,
                 is_string: bool = False) -> int:
    """Return the wire byte size of a single field, recursing into nested structs.
    In fixed mode, arrays use their full declared size.
    In variable mode, only string arrays (char[N], wchar_t[N]) include a length
    prefix; non-string arrays always use their full declared size."""
    if wire_type in WIRE_TYPE_SIZES:
        element_size = WIRE_TYPE_SIZES[wire_type]
    elif wire_type in structs:
        if wire_type in _visiting:
            error(f"Circular struct reference detected involving '{wire_type}'")
        element_size = struct_wire_size(wire_type, structs, _visiting | {wire_type}, fixed_mode=fixed_mode)
    else:
        error(f"Cannot determine wire size for type '{wire_type}'")

    count = array_len if array_len is not None else 1
    size = element_size * count
    if not fixed_mode and array_len is not None and is_string:
        size += length_prefix_size(array_len)
    return size


def _field_is_string(f: dict) -> bool:
    """Return True if this field is a string (char[N] or wchar_t[N])."""
    return f['array_len'] is not None and f['type'] in ('char', 'wchar_t')


def struct_wire_size(struct_name: str, structs: dict,
                     _visiting: frozenset = frozenset(), fixed_mode: bool = True) -> int:
    """Return the total wire byte size of a single instance of struct_name."""
    return sum(
        wire_size_of(f['wire_type'], f['array_len'], structs, _visiting,
                     fixed_mode=fixed_mode, is_string=_field_is_string(f))
        for f in structs[struct_name]['fields']
    )


def compute_max_wire_size(structs: dict, fixed_mode: bool = True) -> int:
    """Return the maximum wire size across all top-level structs."""
    if not structs:
        return 0
    return max(struct_wire_size(name, structs, fixed_mode=fixed_mode) for name in structs)

# ---------------------------------------------------------------------------
# Code generation
# ---------------------------------------------------------------------------


def read_fn_name(user_prefix, struct_name):
    return f"{user_prefix}{type_fn_suffix(struct_name)}_read"


def write_fn_name(user_prefix, struct_name):
    return f"{user_prefix}{type_fn_suffix(struct_name)}_write"


def size_fn_name(user_prefix, struct_name):
    return f"{user_prefix}{type_fn_suffix(struct_name)}_size"


def gen_read_call(prefix, field, accessor, all_struct_names, indent="    ", endian_suffix="_le"):
    wt = field['wire_type']
    if wt in all_struct_names:
        fn = read_fn_name(prefix, wt)
        return [f"{indent}res = {fn}(&{accessor}, on_read, on_read_state);",
                f"{indent}if(res < 0) {{ return res; }}",
                f"{indent}bytes_read += res;"]
    elif field['is_enum']:
        wt = field['type']
        return [f"{indent}res = read_{wt}(&{accessor}, on_read, on_read_state, &bytes_read);",
                f"{indent}if(res < 0) {{ return res; }}"]
    else:
        c_type = field['type']
        sfx = endian_suffix_for_type(c_type, endian_suffix)
        return [f"{indent}res = buffers_read_{c_type}{sfx}(&{accessor}, on_read, on_read_state, &bytes_read);",
                f"{indent}if(res < 0) {{ return res; }}"]


def gen_write_call(prefix, field, accessor, all_struct_names, indent="    ", endian_suffix="_le"):
    wt = field['wire_type']
    if wt in all_struct_names:
        fn = write_fn_name(prefix, wt)
        return [f"{indent}res = {fn}(&{accessor}, on_write, on_write_state);",
                f"{indent}if(res < 0) {{ return res; }}",
                f"{indent}total += res;"]
    elif field['is_enum']:
        wt = field['type']
        return [f"{indent}res = write_{wt}({accessor}, on_write, on_write_state);",
                f"{indent}if(res < 0) {{ return res; }}",
                f"{indent}total += res;"]
    else:
        c_type = field['type']
        sfx = endian_suffix_for_type(c_type, endian_suffix)
        return [f"{indent}res = buffers_write_{c_type}{sfx}({accessor}, on_write, on_write_state);",
                f"{indent}if(res < 0) {{ return res; }}",
                f"{indent}total += res;"]

def gen_enum_write_fn(enum_name, wire_type, endian_suffix="_le"):
    fn = f"write_{enum_name}"
    sfx = endian_suffix_for_type(wire_type, endian_suffix)
    lines = [f"static int {fn}({enum_name} e, buffers_write_callback_t on_write, void* on_write_state) {{"]
    lines.append(f"    {wire_type} tmp = ({wire_type})e;")
    lines.append(f"    return buffers_write_{wire_type}{sfx}(tmp, on_write, on_write_state);")
    lines.append("}")
    return "\n".join(lines)

def gen_write_fn(prefix, struct_name, fields, all_struct_names, fixed_mode=True, endian_suffix="_le"):
    fn = write_fn_name(prefix, struct_name)
    lines = [f"int {fn}(const {struct_name}* s, buffers_write_callback_t on_write, void* on_write_state) {{"]
    if not fields:
        lines.append("    (void)s; (void)on_write; (void)on_write_state;")
        lines.append("    return 0;")
    else:
        lines.append("    int res;")
        lines.append("    int total = 0;")
        for i, f in enumerate(fields):
            if f['array_len'] is not None:
                is_string = _field_is_string(f)
                if fixed_mode or not is_string:
                    # Fixed mode, or non-string array in variable mode:
                    # write all array_len elements with no length prefix
                    lines.append(f"    for(int i = 0; i < {f['array_len']}; ++i) {{")
                    stmts = gen_write_call(prefix, f, f"s->{f['name']}[i]", all_struct_names, indent="        ", endian_suffix=endian_suffix)
                    lines.extend(stmts)
                    lines.append("    }")
                else:
                    # Variable mode, string field: compute length, write prefix, then elements
                    lp_type = length_prefix_type(f['array_len'])
                    lines.append(f"    {{")
                    lines.append(f"        {lp_type} _len_{f['name']} = 0;")
                    lines.append(f"        for(int i = 0; i < {f['array_len']}; ++i) {{")
                    if f['type'] == 'char':
                        lines.append(f"            if(s->{f['name']}[i] == '\\0') break;")
                    else:
                        # wchar_t
                        lines.append(f"            if(s->{f['name']}[i] == L'\\0') break;")
                    lines.append(f"            _len_{f['name']}++;")
                    lines.append(f"        }}")
                    lines.append(f"        res = buffers_write_{lp_type}{endian_suffix_for_type(lp_type, endian_suffix)}(_len_{f['name']}, on_write, on_write_state);")
                    lines.append(f"        if(res < 0) {{ return res; }}")
                    lines.append(f"        total += res;")
                    lines.append(f"        for(int i = 0; i < (int)_len_{f['name']}; ++i) {{")
                    stmts = gen_write_call(prefix, f, f"s->{f['name']}[i]", all_struct_names, indent="            ", endian_suffix=endian_suffix)
                    lines.extend(stmts)
                    lines.append(f"        }}")
                    lines.append(f"    }}")
            else:
                stmts = gen_write_call(prefix, f, f"s->{f['name']}", all_struct_names, endian_suffix=endian_suffix)
                lines.extend(stmts)
        lines.append("    return total;")
    lines.append("}")
    return "\n".join(lines)


def gen_size_fn(prefix, struct_name, fields, all_struct_names):
    """Generate a function that computes the actual wire size of a struct instance
    (only used in variable-length mode)."""
    fn = size_fn_name(prefix, struct_name)
    lines = [f"size_t {fn}(const {struct_name}* s) {{"]
    if not fields:
        lines.append("    (void)s;")
        lines.append("    return 0;")
        lines.append("}")
        return "\n".join(lines)
    lines.append("    size_t size = 0;")
    for f in fields:
        if f['array_len'] is not None:
            is_string = _field_is_string(f)
            wt = f['wire_type']

            if not is_string:
                # Non-string array: no length prefix, always full declared size
                if wt in all_struct_names:
                    nested_size_fn = size_fn_name(prefix, wt)
                    lines.append(f"    {{")
                    lines.append(f"        for(int i = 0; i < {f['array_len']}; ++i) {{")
                    lines.append(f"            size += {nested_size_fn}(&s->{f['name']}[i]);")
                    lines.append(f"        }}")
                    lines.append(f"    }}")
                else:
                    elem_sz = WIRE_TYPE_SIZES.get(wt, 0)
                    lines.append(f"    size += (size_t){f['array_len']} * {elem_sz};")
            else:
                # String array: length prefix + strlen (up to array_len)
                lp_type = length_prefix_type(f['array_len'])
                lp_sz = WIRE_TYPE_SIZES[lp_type]
                elem_sz = WIRE_TYPE_SIZES.get(wt, 0)
                lines.append(f"    {{")
                lines.append(f"        {lp_type} _len = 0;")
                lines.append(f"        for(int i = 0; i < {f['array_len']}; ++i) {{")
                if f['type'] == 'char':
                    lines.append(f"            if(s->{f['name']}[i] == '\\0') break;")
                else:
                    lines.append(f"            if(s->{f['name']}[i] == L'\\0') break;")
                lines.append(f"            _len++;")
                lines.append(f"        }}")
                lines.append(f"        size += {lp_sz} + (size_t)_len * {elem_sz};")
                lines.append(f"    }}")
        else:
            wt = f['wire_type']
            if wt in all_struct_names:
                nested_size_fn = size_fn_name(prefix, wt)
                lines.append(f"    size += {nested_size_fn}(&s->{f['name']});")
            else:
                sz = WIRE_TYPE_SIZES.get(wt, 0)
                lines.append(f"    size += {sz};")
    lines.append("    return size;")
    lines.append("}")
    return "\n".join(lines)


def gen_enum_read_fn(enum_name, wire_type, endian_suffix="_le"):
    fn = f"read_{enum_name}"
    sfx = endian_suffix_for_type(wire_type, endian_suffix)
    lines = [f"static int {fn}({enum_name}* e, buffers_read_callback_t on_read, void* on_read_state, int* bytes_read) {{"]
    lines.append(f"    {wire_type} tmp;")
    lines.append(f"    int res = buffers_read_{wire_type}{sfx}(&tmp, on_read, on_read_state, bytes_read);")
    lines.append("    if(res < 0) { return res; }")
    lines.append(f"    *e = ({enum_name})tmp;")
    lines.append("    return 0;")
    lines.append("}")
    return "\n".join(lines)

def gen_read_fn(prefix, struct_name, fields, all_struct_names, fixed_mode=True, endian_suffix="_le"):
    fn = read_fn_name(prefix, struct_name)
    lines = [f"int {fn}({struct_name}* s, buffers_read_callback_t on_read, void* on_read_state) {{"]
    if not fields:
        lines.append("    (void)s; (void)on_read; (void)on_read_state;")
        lines.append("    return 0;")
    else:
        lines.append("    int res;")
        lines.append("    int bytes_read = 0;")
        for i, f in enumerate(fields):
            if f['array_len'] is not None:
                is_string = _field_is_string(f)
                if fixed_mode or not is_string:
                    # Fixed mode, or non-string array in variable mode:
                    # read all array_len elements with no length prefix
                    lines.append(f"    for(int i = 0; i < {f['array_len']}; ++i) {{")
                    stmts = gen_read_call(prefix, f, f"s->{f['name']}[i]", all_struct_names, indent="        ", endian_suffix=endian_suffix)
                    lines.extend(stmts)
                    lines.append("    }")
                else:
                    # Variable mode, string field: read length prefix, then that many elements
                    lp_type = length_prefix_type(f['array_len'])
                    lp_cs = lp_type  # e.g. uint8_t, uint16_t, uint32_t
                    lines.append(f"    {{")
                    lines.append(f"        {lp_cs} _len_{f['name']};")
                    lines.append(f"        res = buffers_read_{lp_cs}{endian_suffix_for_type(lp_cs, endian_suffix)}(&_len_{f['name']}, on_read, on_read_state, &bytes_read);")
                    lines.append(f"        if(res < 0) {{ return res; }}")
                    lines.append(f"        if(_len_{f['name']} > {f['array_len']}) {{ return BUFFERS_ERROR_EOF; }}")
                    lines.append(f"        for(int i = 0; i < (int)_len_{f['name']}; ++i) {{")
                    stmts = gen_read_call(prefix, f, f"s->{f['name']}[i]", all_struct_names, indent="            ", endian_suffix=endian_suffix)
                    lines.extend(stmts)
                    lines.append(f"        }}")
                    # Place null terminator if there's room
                    null_lit = "L'\\0'" if f['type'] == 'wchar_t' else "'\\0'"
                    lines.append(f"        if(_len_{f['name']} < {f['array_len']}) {{")
                    lines.append(f"            s->{f['name']}[_len_{f['name']}] = {null_lit};")
                    lines.append(f"        }}")
                    lines.append(f"    }}")
            else:
                stmts = gen_read_call(prefix, f, f"s->{f['name']}", all_struct_names, endian_suffix=endian_suffix)
                lines.extend(stmts)
        lines.append("    return bytes_read;")
    lines.append("}")
    return "\n".join(lines)




def generate_h(header_path, user_prefix, structs, fixed_mode=True):
    stem = os.path.splitext(os.path.basename(header_path))[0]
    guard = f"{stem.upper()}_BUFFERS_H"
    define_prefix = header_stem_to_define_prefix(header_path)
    max_size = compute_max_wire_size(structs, fixed_mode=fixed_mode)
    lines = [
        f"#ifndef {guard}",
        f"#define {guard}",
        f'#include "{os.path.basename(header_path)}"',
        '#include "buffers.h"',
        "",
        f"#define {define_prefix}_MAX_SIZE ({max_size})",
    ]
    for struct_name in structs:
        size = struct_wire_size(struct_name, structs, fixed_mode=fixed_mode)
        define = struct_size_define_name(struct_name, user_prefix)
        lines.append(f"#define {define} ({size})")
    lines += [
        "",
        "#ifdef __cplusplus",
        'extern "C" {',
        "#endif",
        "",
    ]
    for struct_name in structs:
        lines.append(f"int {read_fn_name(user_prefix, struct_name)}({struct_name}* s, buffers_read_callback_t on_read, void* on_read_state);")
        lines.append(f"int {write_fn_name(user_prefix, struct_name)}(const {struct_name}* s, buffers_write_callback_t on_write, void* on_write_state);")
        if not fixed_mode:
            lines.append(f"size_t {size_fn_name(user_prefix, struct_name)}(const {struct_name}* s);")
        lines.append("")
    lines += ["#ifdef __cplusplus", "}", "#endif", f"#endif /* {guard} */", ""]
    return "\n".join(lines)


def generate_c(header_path, user_prefix, structs, fixed_mode=True, endian_suffix="_le"):
    stem = os.path.splitext(os.path.basename(header_path))[0]
    all_struct_names = set(structs.keys())
    lines = [
        '#include "buffers.h"',
        f'#include "{stem}_buffers.h"',
        "",
    ]
    enum_types = dict()
    for struct_name, info in structs.items():
        for field in info['fields']:
            if field['is_enum']:
              enum_types[field['type']]=field['wire_type']

    for enum_name, wire_type in enum_types.items():
        lines.append(gen_enum_read_fn(enum_name, wire_type, endian_suffix=endian_suffix))
        lines.append("")
        lines.append(gen_enum_write_fn(enum_name, wire_type, endian_suffix=endian_suffix))
        lines.append("")

    for struct_name, info in structs.items():
        lines.append(gen_read_fn(user_prefix, struct_name, info['fields'], all_struct_names, fixed_mode=fixed_mode, endian_suffix=endian_suffix))
        lines.append("")
        lines.append(gen_write_fn(user_prefix, struct_name, info['fields'], all_struct_names, fixed_mode=fixed_mode, endian_suffix=endian_suffix))
        lines.append("")
        if not fixed_mode:
            lines.append(gen_size_fn(user_prefix, struct_name, info['fields'], all_struct_names))
            lines.append("")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

BUFFERS_H_CONTENT = r"""#ifndef HTCW_BUFFERS_H
#define HTCW_BUFFERS_H
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <wchar.h>
#include <string.h>  /* memcpy */
#ifdef __cplusplus
extern "C" {
#endif

enum {
    BUFFERS_ERROR_EOF = -2,
    BUFFERS_EOF       = -1,
    BUFFERS_SUCCESS   =  0
};

typedef int  (*buffers_read_callback_t )(void* state);
typedef int  (*buffers_write_callback_t)(uint8_t value, void* state);

/* -------------------------------------------------------------------------
 * Read/write functions - single-byte (no byte order)
 * ------------------------------------------------------------------------- */
int buffers_read_uint8_t (uint8_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int8_t  (int8_t*   result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_write_uint8_t(uint8_t   value,  buffers_write_callback_t cb, void* state);
int buffers_write_int8_t (int8_t    value,  buffers_write_callback_t cb, void* state);
/* single-byte aliases */
int buffers_read_char         (char*          result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_char(unsigned char* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_bool         (bool*          result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_write_char         (char          value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_char(unsigned char value, buffers_write_callback_t cb, void* state);
int buffers_write_bool         (bool          value, buffers_write_callback_t cb, void* state);

/* -------------------------------------------------------------------------
 * Read functions - little-endian (_le variants)
 * ------------------------------------------------------------------------- */
int buffers_read_uint16_t_le(uint16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_uint32_t_le(uint32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_uint64_t_le(uint64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int16_t_le (int16_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int32_t_le (int32_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int64_t_le (int64_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_float_le   (float*    result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_double_le  (double*   result, buffers_read_callback_t cb, void* state, int* bytes_read);
/* aliases */
int buffers_read_short_le         (short*              result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_short_le(unsigned short*     result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int_le           (int*                result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_int_le  (unsigned int*       result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_long_le          (long*               result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_long_le (unsigned long*      result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_long_long_le         (long long*          result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_unsigned_long_long_le(unsigned long long* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_wchar_t_le       (wchar_t*            result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_size_t_le        (size_t*             result, buffers_read_callback_t cb, void* state, int* bytes_read);

/* -------------------------------------------------------------------------
 * Read functions - big-endian (_be variants)
 * ------------------------------------------------------------------------- */
int buffers_read_uint16_t_be(uint16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_uint32_t_be(uint32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_uint64_t_be(uint64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int16_t_be (int16_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int32_t_be (int32_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_int64_t_be (int64_t*  result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_float_be   (float*    result, buffers_read_callback_t cb, void* state, int* bytes_read);
int buffers_read_double_be  (double*   result, buffers_read_callback_t cb, void* state, int* bytes_read);

/* -------------------------------------------------------------------------
 * Write functions - little-endian (_le variants)
 * ------------------------------------------------------------------------- */
int buffers_write_uint16_t_le(uint16_t value, buffers_write_callback_t cb, void* state);
int buffers_write_uint32_t_le(uint32_t value, buffers_write_callback_t cb, void* state);
int buffers_write_uint64_t_le(uint64_t value, buffers_write_callback_t cb, void* state);
int buffers_write_int16_t_le (int16_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_int32_t_le (int32_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_int64_t_le (int64_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_float_le   (float    value, buffers_write_callback_t cb, void* state);
int buffers_write_double_le  (double   value, buffers_write_callback_t cb, void* state);
/* aliases */
int buffers_write_short_le         (short              value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_short_le(unsigned short     value, buffers_write_callback_t cb, void* state);
int buffers_write_int_le           (int                value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_int_le  (unsigned int       value, buffers_write_callback_t cb, void* state);
int buffers_write_long_le          (long               value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_long_le (unsigned long      value, buffers_write_callback_t cb, void* state);
int buffers_write_long_long_le         (long long          value, buffers_write_callback_t cb, void* state);
int buffers_write_unsigned_long_long_le(unsigned long long value, buffers_write_callback_t cb, void* state);
int buffers_write_wchar_t_le       (wchar_t            value, buffers_write_callback_t cb, void* state);
int buffers_write_size_t_le        (size_t             value, buffers_write_callback_t cb, void* state);

/* -------------------------------------------------------------------------
 * Write functions - big-endian
 * ------------------------------------------------------------------------- */
int buffers_write_uint16_t_be(uint16_t value, buffers_write_callback_t cb, void* state);
int buffers_write_uint32_t_be(uint32_t value, buffers_write_callback_t cb, void* state);
int buffers_write_uint64_t_be(uint64_t value, buffers_write_callback_t cb, void* state);
int buffers_write_int16_t_be (int16_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_int32_t_be (int32_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_int64_t_be (int64_t  value, buffers_write_callback_t cb, void* state);
int buffers_write_float_be   (float    value, buffers_write_callback_t cb, void* state);
int buffers_write_double_be  (double   value, buffers_write_callback_t cb, void* state);

#ifdef __cplusplus
}
#endif
#endif /* HTCW_BUFFERS_H */
"""

BUFFERS_C_CONTENT = r"""#include "buffers.h"

/* =========================================================================
 * Internal helpers
 * ========================================================================= */

static int read_byte(buffers_read_callback_t cb, void* state, uint8_t* out, int* bytes_read) {
    int b = cb(state);
    if (b == -1) return BUFFERS_ERROR_EOF;
    if (b  <  0) return b;
    *out = (uint8_t)b;
    if (bytes_read) ++(*bytes_read);
    return 0;
}

static int write_byte(uint8_t v, buffers_write_callback_t cb, void* state) {
    int r = cb(v, state);
    if (r < 0) return r;
    return 1;
}

/* =========================================================================
 * uint8_t  (single byte - no byte order)
 * ========================================================================= */
int buffers_read_uint8_t(uint8_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    return read_byte(cb, state, result, bytes_read);
}
int buffers_write_uint8_t(uint8_t value, buffers_write_callback_t cb, void* state) {
    return write_byte(value, cb, state);
}

/* =========================================================================
 * int8_t  (single byte - no byte order)
 * ========================================================================= */
int buffers_read_int8_t(int8_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t tmp; int r = read_byte(cb, state, &tmp, bytes_read); if (r < 0) return r;
    *result = (int8_t)tmp; return 0;
}
int buffers_write_int8_t(int8_t value, buffers_write_callback_t cb, void* state) {
    return write_byte((uint8_t)value, cb, state);
}

/* =========================================================================
 * uint16_t  - little-endian: low byte first
 * ========================================================================= */
int buffers_read_uint16_t_le(uint16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t lo, hi; int r;
    r = read_byte(cb, state, &lo, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &hi, bytes_read); if (r < 0) return r;
    *result = (uint16_t)((hi << 8) | lo);
    return 0;
}
int buffers_write_uint16_t_le(uint16_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = write_byte((uint8_t)(value      ), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >> 8 ), cb, state); if (r < 0) return r; total += r;
    return total;
}

/* big-endian */
int buffers_read_uint16_t_be(uint16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t hi, lo; int r;
    r = read_byte(cb, state, &hi, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &lo, bytes_read); if (r < 0) return r;
    *result = (uint16_t)((hi << 8) | lo);
    return 0;
}
int buffers_write_uint16_t_be(uint16_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = write_byte((uint8_t)(value >> 8), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value     ), cb, state); if (r < 0) return r; total += r;
    return total;
}

/* =========================================================================
 * int16_t
 * ========================================================================= */
int buffers_read_int16_t_le(int16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint16_t tmp; int r = buffers_read_uint16_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int16_t)tmp; return 0;
}
int buffers_write_int16_t_le(int16_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint16_t_le((uint16_t)value, cb, state);
}
int buffers_read_int16_t_be(int16_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint16_t tmp; int r = buffers_read_uint16_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int16_t)tmp; return 0;
}
int buffers_write_int16_t_be(int16_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint16_t_be((uint16_t)value, cb, state);
}

/* =========================================================================
 * uint32_t
 * ========================================================================= */
int buffers_read_uint32_t_le(uint32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t b0, b1, b2, b3; int r;
    r = read_byte(cb, state, &b0, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b1, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b2, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b3, bytes_read); if (r < 0) return r;
    *result = ((uint32_t)b3 << 24) | ((uint32_t)b2 << 16) |
              ((uint32_t)b1 <<  8) |  (uint32_t)b0;
    return 0;
}
int buffers_write_uint32_t_le(uint32_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = write_byte((uint8_t)(value      ), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >>  8), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >> 16), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >> 24), cb, state); if (r < 0) return r; total += r;
    return total;
}
int buffers_read_uint32_t_be(uint32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint8_t b0, b1, b2, b3; int r;
    r = read_byte(cb, state, &b0, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b1, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b2, bytes_read); if (r < 0) return r;
    r = read_byte(cb, state, &b3, bytes_read); if (r < 0) return r;
    *result = ((uint32_t)b0 << 24) | ((uint32_t)b1 << 16) |
              ((uint32_t)b2 <<  8) |  (uint32_t)b3;
    return 0;
}
int buffers_write_uint32_t_be(uint32_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = write_byte((uint8_t)(value >> 24), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >> 16), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value >>  8), cb, state); if (r < 0) return r; total += r;
    r = write_byte((uint8_t)(value      ), cb, state); if (r < 0) return r; total += r;
    return total;
}

/* =========================================================================
 * int32_t
 * ========================================================================= */
int buffers_read_int32_t_le(int32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t tmp; int r = buffers_read_uint32_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int32_t)tmp; return 0;
}
int buffers_write_int32_t_le(int32_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint32_t_le((uint32_t)value, cb, state);
}
int buffers_read_int32_t_be(int32_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t tmp; int r = buffers_read_uint32_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int32_t)tmp; return 0;
}
int buffers_write_int32_t_be(int32_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint32_t_be((uint32_t)value, cb, state);
}

/* =========================================================================
 * uint64_t
 * ========================================================================= */
int buffers_read_uint64_t_le(uint64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t lo, hi; int r;
    r = buffers_read_uint32_t_le(&lo, cb, state, bytes_read); if (r < 0) return r;
    r = buffers_read_uint32_t_le(&hi, cb, state, bytes_read); if (r < 0) return r;
    *result = ((uint64_t)hi << 32) | lo;
    return 0;
}
int buffers_write_uint64_t_le(uint64_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = buffers_write_uint32_t_le((uint32_t)(value      ), cb, state); if (r < 0) return r; total += r;
    r = buffers_write_uint32_t_le((uint32_t)(value >> 32), cb, state); if (r < 0) return r; total += r;
    return total;
}
int buffers_read_uint64_t_be(uint64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t hi, lo; int r;
    r = buffers_read_uint32_t_be(&hi, cb, state, bytes_read); if (r < 0) return r;
    r = buffers_read_uint32_t_be(&lo, cb, state, bytes_read); if (r < 0) return r;
    *result = ((uint64_t)hi << 32) | lo;
    return 0;
}
int buffers_write_uint64_t_be(uint64_t value, buffers_write_callback_t cb, void* state) {
    int r, total = 0;
    r = buffers_write_uint32_t_be((uint32_t)(value >> 32), cb, state); if (r < 0) return r; total += r;
    r = buffers_write_uint32_t_be((uint32_t)(value      ), cb, state); if (r < 0) return r; total += r;
    return total;
}

/* =========================================================================
 * int64_t
 * ========================================================================= */
int buffers_read_int64_t_le(int64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint64_t tmp; int r = buffers_read_uint64_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int64_t)tmp; return 0;
}
int buffers_write_int64_t_le(int64_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint64_t_le((uint64_t)value, cb, state);
}
int buffers_read_int64_t_be(int64_t* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint64_t tmp; int r = buffers_read_uint64_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    *result = (int64_t)tmp; return 0;
}
int buffers_write_int64_t_be(int64_t value, buffers_write_callback_t cb, void* state) {
    return buffers_write_uint64_t_be((uint64_t)value, cb, state);
}

/* =========================================================================
 * float  (IEEE 754, reinterpreted as uint32_t)
 * ========================================================================= */
int buffers_read_float_le(float* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t tmp; int r = buffers_read_uint32_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    memcpy(result, &tmp, sizeof(float)); return 0;
}
int buffers_write_float_le(float value, buffers_write_callback_t cb, void* state) {
    uint32_t tmp; memcpy(&tmp, &value, sizeof(float));
    return buffers_write_uint32_t_le(tmp, cb, state);
}
int buffers_read_float_be(float* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint32_t tmp; int r = buffers_read_uint32_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    memcpy(result, &tmp, sizeof(float)); return 0;
}
int buffers_write_float_be(float value, buffers_write_callback_t cb, void* state) {
    uint32_t tmp; memcpy(&tmp, &value, sizeof(float));
    return buffers_write_uint32_t_be(tmp, cb, state);
}

/* =========================================================================
 * double  (IEEE 754, reinterpreted as uint64_t)
 * ========================================================================= */
int buffers_read_double_le(double* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint64_t tmp; int r = buffers_read_uint64_t_le(&tmp, cb, state, bytes_read); if (r < 0) return r;
    memcpy(result, &tmp, sizeof(double)); return 0;
}
int buffers_write_double_le(double value, buffers_write_callback_t cb, void* state) {
    uint64_t tmp; memcpy(&tmp, &value, sizeof(double));
    return buffers_write_uint64_t_le(tmp, cb, state);
}
int buffers_read_double_be(double* result, buffers_read_callback_t cb, void* state, int* bytes_read) {
    uint64_t tmp; int r = buffers_read_uint64_t_be(&tmp, cb, state, bytes_read); if (r < 0) return r;
    memcpy(result, &tmp, sizeof(double)); return 0;
}
int buffers_write_double_be(double value, buffers_write_callback_t cb, void* state) {
    uint64_t tmp; memcpy(&tmp, &value, sizeof(double));
    return buffers_write_uint64_t_be(tmp, cb, state);
}

/* =========================================================================
 * Alias functions (delegate to the wire-type counterpart)
 * ========================================================================= */

int buffers_read_char(char* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int8_t((int8_t*)r, cb, s, bytes_read); }
int buffers_write_char(char v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int8_t((int8_t)v, cb, s); }

int buffers_read_unsigned_char(unsigned char* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint8_t((uint8_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_char(unsigned char v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint8_t((uint8_t)v, cb, s); }

int buffers_read_short_le(short* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int16_t_le((int16_t*)r, cb, s, bytes_read); }
int buffers_write_short_le(short v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int16_t_le((int16_t)v, cb, s); }

int buffers_read_unsigned_short_le(unsigned short* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint16_t_le((uint16_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_short_le(unsigned short v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint16_t_le((uint16_t)v, cb, s); }

int buffers_read_int_le(int* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int32_t_le((int32_t*)r, cb, s, bytes_read); }
int buffers_write_int_le(int v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int32_t_le((int32_t)v, cb, s); }

int buffers_read_unsigned_int_le(unsigned int* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint32_t_le((uint32_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_int_le(unsigned int v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint32_t_le((uint32_t)v, cb, s); }

int buffers_read_long_le(long* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int32_t_le((int32_t*)r, cb, s, bytes_read); }
int buffers_write_long_le(long v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int32_t_le((int32_t)v, cb, s); }

int buffers_read_unsigned_long_le(unsigned long* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint32_t_le((uint32_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_long_le(unsigned long v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint32_t_le((uint32_t)v, cb, s); }

int buffers_read_long_long_le(long long* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int64_t_le((int64_t*)r, cb, s, bytes_read); }
int buffers_write_long_long_le(long long v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int64_t_le((int64_t)v, cb, s); }

int buffers_read_unsigned_long_long_le(unsigned long long* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_uint64_t_le((uint64_t*)r, cb, s, bytes_read); }
int buffers_write_unsigned_long_long_le(unsigned long long v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint64_t_le((uint64_t)v, cb, s); }

int buffers_read_bool(bool* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    uint8_t tmp; int res = buffers_read_uint8_t(&tmp, cb, s, bytes_read); if (res < 0) return res;
    *r = tmp ? 1 : 0; return 0; }
int buffers_write_bool(bool v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint8_t(v ? 1 : 0, cb, s); }

int buffers_read_wchar_t_le(wchar_t* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    return buffers_read_int16_t_le((int16_t*)r, cb, s, bytes_read); }
int buffers_write_wchar_t_le(wchar_t v, buffers_write_callback_t cb, void* s) {
    return buffers_write_int16_t_le((int16_t)v, cb, s); }

int buffers_read_size_t_le(size_t* r, buffers_read_callback_t cb, void* s, int* bytes_read) {
    uint32_t tmp; int res = buffers_read_uint32_t_le(&tmp, cb, s, bytes_read); if (res < 0) return res;
    *r = (size_t)tmp; return 0; }
int buffers_write_size_t_le(size_t v, buffers_write_callback_t cb, void* s) {
    return buffers_write_uint32_t_le((uint32_t)v, cb, s); }
"""


def main():
    args = sys.argv[1:]
    gen_buffers = False
    out_dir = ""
    user_prefix = ""
    fixed_mode = False  # Default: variable-length (length-prefixed) serialization
    big_endian = False
    while args and args[0].startswith('--'):
        opt = args.pop(0)
        if opt == '--buffers':
            gen_buffers = True
        elif opt == '--out':
            if not args:
                error("--out requires an argument")
            out_dir = args.pop(0)
        elif opt == '--prefix':
            if not args:
                error("--prefix requires an argument")
            user_prefix = args.pop(0)
        elif opt == '--fixed':
            fixed_mode = True
        elif opt == '--big-endian':
            big_endian = True
        else:
            error(f"Unknown option: {opt}")

    if len(args) != 1:
        print(f"Usage: {sys.argv[0]} [--fixed] [--big-endian] [--buffers] [--out <dir>] [--prefix <pfx>] <header.h>", file=sys.stderr)
        sys.exit(1)

    endian_suffix = "_be" if big_endian else "_le"

    path = args[0]
    try:
        with open(path, 'r') as f:
            text = f.read()
    except OSError as e:
        error(f"Cannot open file: {e}")

    structs = parse_header(text)
    if not structs:
        error("No structs found in header")

    stem = os.path.splitext(os.path.basename(path))[0]

    if len(out_dir) == 0:
        out_dir = os.path.dirname(path) or '.'

    h_path = os.path.join(out_dir, f"{stem}_buffers.h")
    c_path = os.path.join(out_dir, f"{stem}_buffers.c")

    with open(h_path, 'w') as f:
        f.write(generate_h(path, user_prefix, structs, fixed_mode=fixed_mode))
    with open(c_path, 'w') as f:
        f.write(generate_c(path, user_prefix, structs, fixed_mode=fixed_mode, endian_suffix=endian_suffix))

    print(f"Written: {h_path}")
    print(f"Written: {c_path}")

    if gen_buffers:
        bh_path = os.path.join(out_dir, "buffers.h")
        bc_path = os.path.join(out_dir, "buffers.c")
        with open(bh_path, 'w') as f:
            f.write(BUFFERS_H_CONTENT.lstrip('\n'))
        with open(bc_path, 'w') as f:
            f.write(BUFFERS_C_CONTENT.lstrip('\n'))
        print(f"Written: {bh_path}")
        print(f"Written: {bc_path}")


if __name__ == '__main__':
    main()