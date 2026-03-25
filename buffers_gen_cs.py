#!/usr/bin/env python3
"""
buffers_gen_cs.py - Parse wire structs from a C header and generate
                    C# read/write methods for each struct.

Usage: python buffers_gen_cs.py [options] <header.h>

Options:
  --fixed            Use fixed-size serialization for arrays and strings
                    (transmit entire declared size). Without this flag,
                    arrays/strings are length-prefixed on the wire.
  --big-endian      Generate struct read/write methods using big-endian
                    serialization. Without this flag, little-endian is used.
  --namespace <Ns>   File-scoped namespace for generated code (default: none)
  --public           Emit public visibility (default: implicit internal)

Outputs:
  <StemName>Buffers.cs  - partial structs + enums with TryRead/TryWrite
  Buffers.cs            - shared supporting code (namespace Htcw)

Naming:
  - snake_case, SCREAM_CASE, camelCase -> .NET conventions
    * 1-2 char words: ALL CAPS  (e.g. ip, id -> IP, ID)
    * 3+ char words: PascalCase (e.g. address -> Address)
  - _t suffix stripped from typedef names before conversion
  - TryRead/TryWrite methods have no LE/BE suffix regardless of endianness
"""

import os
import re
import sys

# ---------------------------------------------------------------------------
# Type mapping  (C type -> C# type, wire-type -> C# type)
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

# Map C wire types to C# primitive types
CS_TYPE_MAP = {
    'uint8_t':  'byte',
    'uint16_t': 'ushort',
    'uint32_t': 'uint',
    'uint64_t': 'ulong',
    'int8_t':   'sbyte',
    'int16_t':  'short',
    'int32_t':  'int',
    'int64_t':  'long',
    'float':    'float',
    'double':   'double',
}

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

WIRE_SCALAR_TYPES = set(WIRE_TYPE_SIZES.keys())

# Types that don't need BinaryPrimitives (single byte)
SINGLE_BYTE_TYPES = {'uint8_t', 'int8_t'}

# Map from original C type to preferred C# interface type, where it differs
# from the wire-derived type.  Only entries that need special handling.
CS_NATIVE_TYPE_MAP = {
    'bool':    'bool',
    'char':    'char',
    'wchar_t': 'char',
}

def enum_wire_type(min_val: int, max_val: int) -> str:
    if min_val >= 0:
        if max_val <= 0xFF:           return 'uint8_t'
        elif max_val <= 0xFFFF:       return 'uint16_t'
        elif max_val <= 0xFFFFFFFF:   return 'uint32_t'
        else:                         return 'uint64_t'
    else:
        if   min_val >= -128        and max_val <= 127:        return 'int8_t'
        elif min_val >= -32768      and max_val <= 32767:      return 'int16_t'
        elif min_val >= -2147483648 and max_val <= 2147483647: return 'int32_t'
        else:                                                   return 'int64_t'


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


# ---------------------------------------------------------------------------
# .NET naming helpers
# ---------------------------------------------------------------------------

def split_words(name: str) -> list:
    """
    Split a C identifier (snake_case, SCREAM_CASE, camelCase, PascalCase)
    into a list of lowercase word strings.
    """
    # Strip leading/trailing underscores
    name = name.strip('_')
    # Replace runs of underscores with a single separator
    name = re.sub(r'_+', '_', name)
    # Insert boundary before transitions: lower->upper, upper->upper+lower
    name = re.sub(r'([a-z0-9])([A-Z])', r'\1_\2', name)
    name = re.sub(r'([A-Z]+)([A-Z][a-z])', r'\1_\2', name)
    parts = [p for p in name.split('_') if p]
    return [p.lower() for p in parts]


def dotnet_word(word: str) -> str:
    """Apply .NET casing to a single lowercase word."""
    if len(word) <= 2:
        return word.upper()
    return word.capitalize()


def to_dotnet_name(c_name: str) -> str:
    """Convert a C identifier to .NET PascalCase/ALLCAPS name."""
    # Strip trailing _t (C typedef convention)
    if c_name.endswith('_t'):
        c_name = c_name[:-2]
    words = split_words(c_name)
    if not words:
        return c_name
    parts = []
    for i, w in enumerate(words):
        # Corner case: "is"/"to" as the first word should be "Is"/"To" not "IS"/"TO"
        if i == 0 and w in ('is', 'to'):
            parts.append(w.capitalize())
        else:
            parts.append(dotnet_word(w))
    return ''.join(parts)


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


def cs_wire_type(wire_type: str) -> str:
    return CS_TYPE_MAP.get(wire_type, wire_type)


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
    """
    Returns dict: c_name -> {wire_type, members: [(cs_member_name, int_value)]}
    Bug fix vs original: use group 'n'
    """
    enums = {}
    found = []  # list of (c_name, body)

    for m in ENUM_TYPEDEF_RE.finditer(text):
        found.append((m.group('n'), m.group('body')))
    typedef_names = {n for n, _ in found}
    for m in ENUM_RE.finditer(text):
        if m.group('n') not in typedef_names:
            found.append((m.group('n'), m.group('body')))

    for c_name, body in found:
        if c_name in enums:
            error(f"Duplicate enum name: '{c_name}'")
        members = []
        current = 0
        for entry in body.split(','):
            entry = entry.strip()
            if not entry:
                continue
            if '=' in entry:
                lhs, rhs = entry.split('=', 1)
                lhs = lhs.strip()
                rhs = rhs.strip()
                if not _INT_LITERAL_RE.fullmatch(rhs):
                    error(f"Enum '{c_name}': non-literal value '{rhs}' not supported")
                current = int(rhs, 0)
                member_c_name = lhs
            else:
                member_c_name = entry.strip()
            cs_member = to_dotnet_name(member_c_name)
            members.append((cs_member, current))
            current += 1

        values = [v for _, v in members]
        if values:
            wire_type = enum_wire_type(min(values), max(values))
            enums[c_name] = {'wire_type': wire_type, 'members': members}
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
        return known_enums[type_str]['wire_type']
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
    # Track original C enum type for cast generation
    is_enum = type_str in known_enums
    enum_c_name = type_str if is_enum else None
    return {
        "c_name": name,
        "cs_name": to_dotnet_name(name),
        "type": type_str,
        "wire_type": wire_type,
        "array_len": array_len,
        "is_enum": is_enum,
        "enum_c_name": enum_c_name,
    }


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


def parse_header(text: str) -> tuple:
    """Returns (structs_dict, enums_dict)"""
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
            if f['c_name'] in seen:
                error(f"Struct '{name}': duplicate field name '{f['c_name']}'")
            seen.add(f['c_name'])
        structs[name] = {"fields": fields, "cs_name": to_dotnet_name(name)}

    return structs, known_enums


# ---------------------------------------------------------------------------
# Wire size computation
# ---------------------------------------------------------------------------

def _field_is_string_cs(f: dict) -> bool:
    """Return True if this field is a string (char[N] or wchar_t[N])."""
    return f['array_len'] is not None and f['type'] in ('char', 'wchar_t')


def wire_size_of(wire_type: str, array_len, structs: dict,
                 _visiting: frozenset = frozenset(), fixed_mode: bool = True,
                 is_string: bool = False) -> int:
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


def struct_wire_size(struct_name: str, structs: dict,
                     _visiting: frozenset = frozenset(), fixed_mode: bool = True) -> int:
    return sum(
        wire_size_of(f['wire_type'], f['array_len'], structs, _visiting,
                     fixed_mode=fixed_mode, is_string=_field_is_string_cs(f))
        for f in structs[struct_name]['fields']
    )


def compute_max_wire_size(structs: dict, fixed_mode: bool = True) -> int:
    if not structs:
        return 0
    return max(struct_wire_size(name, structs, fixed_mode=fixed_mode) for name in structs)


# ---------------------------------------------------------------------------
# Field type helpers
# ---------------------------------------------------------------------------

def is_char_array(f: dict) -> bool:
    """char[N] -> string (UTF-8)"""
    return f['array_len'] is not None and f['type'] == 'char'

def is_wchar_array(f: dict) -> bool:
    """wchar_t[N] -> string (UTF-16)"""
    return f['array_len'] is not None and f['type'] == 'wchar_t'

def is_bool(f: dict) -> bool:
    """C bool -> C# bool (wire: byte)"""
    return f['type'] == 'bool'

def is_native_char(f: dict) -> bool:
    """Single char or wchar_t (non-array) -> C# char"""
    return f['array_len'] is None and f['type'] in ('char', 'wchar_t')

def is_struct_array(f: dict, all_struct_names: set) -> bool:
    return f['array_len'] is not None and f['wire_type'] in all_struct_names

def cs_field_type(f: dict, structs: dict) -> str:
    """
    Return the C# type for a field as declared on the class:
      - char[N]       -> string
      - wchar_t[N]    -> string
      - bool          -> bool
      - single char   -> char
      - single wchar_t-> char
      - struct T[N]   -> IList<CsName>
      - scalar[N]     -> cstype[]  (bool[N] -> bool[])
      - enum (scalar) -> EnumCsName
      - struct T      -> CsName
      - scalar        -> cstype
    """
    all_struct_names = set(structs.keys())
    wt = f['wire_type']
    arr = f['array_len']
    c_type = f['type']
    native = CS_NATIVE_TYPE_MAP.get(c_type)

    if arr is not None:
        if is_char_array(f) or is_wchar_array(f):
            return 'string'
        if is_struct_array(f, all_struct_names):
            return f"IList<{to_dotnet_name(wt)}>"
        # scalar or enum array
        if f['is_enum']:
            return f"{to_dotnet_name(f['enum_c_name'])}[]"
        if native:
            return f"{native}[]"
        return f"{cs_wire_type(wt)}[]"
    else:
        if wt in all_struct_names:
            return to_dotnet_name(wt)
        if f['is_enum']:
            return to_dotnet_name(f['enum_c_name'])
        if native:
            return native
        return cs_wire_type(wt)


# ---------------------------------------------------------------------------
# C# code generation helpers
# ---------------------------------------------------------------------------

def bp_read_le(wire_type: str, span_expr: str, offset_expr: str) -> str:
    """Return a BinaryPrimitives LE read expression for the given wire type."""
    if wire_type == 'uint8_t':
        return f"{span_expr}[{offset_expr}]"
    if wire_type == 'int8_t':
        return f"(sbyte){span_expr}[{offset_expr}]"
    cs = CS_TYPE_MAP[wire_type]
    bp_type = {
        'ushort': 'UInt16', 'short': 'Int16',
        'uint':   'UInt32', 'int':   'Int32',
        'ulong':  'UInt64', 'long':  'Int64',
        'float':  'Single', 'double': 'Double',
    }[cs]
    slice_expr = f"{span_expr}.Slice({offset_expr})" if offset_expr != '0' else span_expr
    return f"BinaryPrimitives.Read{bp_type}LittleEndian({slice_expr})"


def bp_read_be(wire_type: str, span_expr: str, offset_expr: str) -> str:
    if wire_type == 'uint8_t':
        return f"{span_expr}[{offset_expr}]"
    if wire_type == 'int8_t':
        return f"(sbyte){span_expr}[{offset_expr}]"
    cs = CS_TYPE_MAP[wire_type]
    bp_type = {
        'ushort': 'UInt16', 'short': 'Int16',
        'uint':   'UInt32', 'int':   'Int32',
        'ulong':  'UInt64', 'long':  'Int64',
        'float':  'Single', 'double': 'Double',
    }[cs]
    slice_expr = f"{span_expr}.Slice({offset_expr})" if offset_expr != '0' else span_expr
    return f"BinaryPrimitives.Read{bp_type}BigEndian({slice_expr})"


def bp_write_le(wire_type: str, span_expr: str, offset_expr: str, value_expr: str) -> str:
    if wire_type == 'uint8_t':
        return f"{span_expr}[{offset_expr}] = {value_expr};"
    if wire_type == 'int8_t':
        return f"{span_expr}[{offset_expr}] = (byte){value_expr};"
    cs = CS_TYPE_MAP[wire_type]
    bp_type = {
        'ushort': 'UInt16', 'short': 'Int16',
        'uint':   'UInt32', 'int':   'Int32',
        'ulong':  'UInt64', 'long':  'Int64',
        'float':  'Single', 'double': 'Double',
    }[cs]
    slice_expr = f"{span_expr}.Slice({offset_expr})" if offset_expr != '0' else span_expr
    return f"BinaryPrimitives.Write{bp_type}LittleEndian({slice_expr}, {value_expr});"


def bp_write_be(wire_type: str, span_expr: str, offset_expr: str, value_expr: str) -> str:
    if wire_type == 'uint8_t':
        return f"{span_expr}[{offset_expr}] = {value_expr};"
    if wire_type == 'int8_t':
        return f"{span_expr}[{offset_expr}] = (byte){value_expr};"
    cs = CS_TYPE_MAP[wire_type]
    bp_type = {
        'ushort': 'UInt16', 'short': 'Int16',
        'uint':   'UInt32', 'int':   'Int32',
        'ulong':  'UInt64', 'long':  'Int64',
        'float':  'Single', 'double': 'Double',
    }[cs]
    slice_expr = f"{span_expr}.Slice({offset_expr})" if offset_expr != '0' else span_expr
    return f"BinaryPrimitives.Write{bp_type}BigEndian({slice_expr}, {value_expr});"


# ---------------------------------------------------------------------------
# Field declarations
# ---------------------------------------------------------------------------

def gen_field_declarations(fields: list, structs: dict, member_vis: str) -> list:
    lines = []
    for f in fields:
        cs_type = cs_field_type(f, structs)
        lines.append(f"    {member_vis}{cs_type} {f['cs_name']} {{ get; set; }}")
    return lines


# ---------------------------------------------------------------------------
# Span-based core method generation
# ---------------------------------------------------------------------------

def gen_span_read_core(cs_struct_name: str, fields: list, structs: dict,
                       big_endian: bool, indent: str = "        ",
                       fixed_mode: bool = True) -> list:
    nested_suffix = ""  # nested struct calls are always unsuffixed
    bp_read = bp_read_be if big_endian else bp_read_le
    all_struct_names = set(structs.keys())
    lines = []
    lines.append(f"{indent}bytesRead = 0;")
    lines.append(f"{indent}result = new {cs_struct_name}();")
    lines.append(f"{indent}int offset = 0;")

    for f in fields:
        cs_field = f['cs_name']
        wt = f['wire_type']
        sz = WIRE_TYPE_SIZES.get(wt, 0)
        arr = f['array_len']
        is_struct = wt in all_struct_names
        inner  = indent + "    "
        inner2 = inner  + "    "

        if arr is not None and is_char_array(f):
            if fixed_mode:
                # UTF-8 string: read arr bytes, decode, strip null terminator
                lines.append(f"{indent}if (span.Length - offset < {arr}) return false;")
                lines.append(f"{indent}result.{cs_field} = Buffers.DecodeUtf8(span.Slice(offset, {arr}));")
                lines.append(f"{indent}offset += {arr};")
            else:
                # Variable: read length prefix, then that many bytes as UTF-8
                lp_wt = length_prefix_type(arr)
                lp_sz = WIRE_TYPE_SIZES[lp_wt]
                lines.append(f"{indent}if (span.Length - offset < {lp_sz}) return false;")
                read_len = bp_read(lp_wt, "span", "offset")
                lines.append(f"{indent}{{")
                lines.append(f"{inner}int _len_{cs_field} = (int)({read_len});")
                lines.append(f"{inner}offset += {lp_sz};")
                lines.append(f"{inner}if (_len_{cs_field} > {arr} || span.Length - offset < _len_{cs_field}) return false;")
                lines.append(f"{inner}result.{cs_field} = Encoding.UTF8.GetString(span.Slice(offset, _len_{cs_field}));")
                lines.append(f"{inner}offset += _len_{cs_field};")
                lines.append(f"{indent}}}")

        elif arr is not None and is_wchar_array(f):
            if fixed_mode:
                # UTF-16LE string: read arr*2 bytes, decode
                byte_count = arr * 2
                lines.append(f"{indent}if (span.Length - offset < {byte_count}) return false;")
                lines.append(f"{indent}result.{cs_field} = Buffers.DecodeUtf16LE(span.Slice(offset, {byte_count}));")
                lines.append(f"{indent}offset += {byte_count};")
            else:
                # Variable: length prefix is count of wchar_t elements, each 2 bytes on wire
                lp_wt = length_prefix_type(arr)
                lp_sz = WIRE_TYPE_SIZES[lp_wt]
                lines.append(f"{indent}if (span.Length - offset < {lp_sz}) return false;")
                read_len = bp_read(lp_wt, "span", "offset")
                lines.append(f"{indent}{{")
                lines.append(f"{inner}int _len_{cs_field} = (int)({read_len});")
                lines.append(f"{inner}offset += {lp_sz};")
                lines.append(f"{inner}int _byteLen_{cs_field} = _len_{cs_field} * 2;")
                lines.append(f"{inner}if (_len_{cs_field} > {arr} || span.Length - offset < _byteLen_{cs_field}) return false;")
                lines.append(f"{inner}result.{cs_field} = Encoding.Unicode.GetString(span.Slice(offset, _byteLen_{cs_field}));")
                lines.append(f"{inner}offset += _byteLen_{cs_field};")
                lines.append(f"{indent}}}")

        elif arr is not None and is_struct_array(f, all_struct_names):
            # IList<T>: always read all declared elements (no length prefix)
            nested_cs = to_dotnet_name(wt)
            lines.append(f"{indent}{{")
            lines.append(f"{inner}var _list_{cs_field} = new List<{nested_cs}>({arr});")
            lines.append(f"{inner}for (int i = 0; i < {arr}; i++)")
            lines.append(f"{inner}{{")
            lines.append(f"{inner2}if (!{nested_cs}.TryRead{nested_suffix}(span.Slice(offset), out {nested_cs} _item_{cs_field}, out int _n_{cs_field})) return false;")
            lines.append(f"{inner2}_list_{cs_field}.Add(_item_{cs_field});")
            lines.append(f"{inner2}offset += _n_{cs_field};")
            lines.append(f"{inner}}}")
            lines.append(f"{inner}result.{cs_field} = _list_{cs_field};")
            lines.append(f"{indent}}}")

        elif arr is not None:
            # scalar or enum array -> T[] (always read all declared elements, no length prefix)
            cs_t = cs_wire_type(wt)
            is_enum = f['is_enum']
            enum_cs = to_dotnet_name(f['enum_c_name']) if is_enum else None
            native = CS_NATIVE_TYPE_MAP.get(f['type'])
            arr_type = enum_cs if is_enum else (native if native else cs_t)
            lines.append(f"{indent}{{")
            lines.append(f"{inner}var _arr_{cs_field} = new {arr_type}[{arr}];")
            lines.append(f"{inner}for (int i = 0; i < {arr}; i++)")
            lines.append(f"{inner}{{")
            lines.append(f"{inner2}if (span.Length - offset < {sz}) return false;")
            read_expr = bp_read(wt, "span", "offset")
            if is_enum:
                lines.append(f"{inner2}_arr_{cs_field}[i] = ({enum_cs}){read_expr};")
            elif f['type'] == 'bool':
                lines.append(f"{inner2}_arr_{cs_field}[i] = {read_expr} != 0;")
            elif native:
                lines.append(f"{inner2}_arr_{cs_field}[i] = ({native}){read_expr};")
            else:
                lines.append(f"{inner2}_arr_{cs_field}[i] = {read_expr};")
            lines.append(f"{inner2}offset += {sz};")
            lines.append(f"{inner}}}")
            lines.append(f"{inner}result.{cs_field} = _arr_{cs_field};")
            lines.append(f"{indent}}}")

        else:
            # single field
            if is_struct:
                nested_cs = to_dotnet_name(wt)
                lines.append(f"{indent}if (!{nested_cs}.TryRead{nested_suffix}(span.Slice(offset), out {nested_cs} _{cs_field}_val, out int _n_{cs_field})) return false;")
                lines.append(f"{indent}result.{cs_field} = _{cs_field}_val;")
                lines.append(f"{indent}offset += _n_{cs_field};")
            else:
                lines.append(f"{indent}if (span.Length - offset < {sz}) return false;")
                read_expr = bp_read(wt, "span", "offset")
                cs_t = cs_wire_type(wt)
                native = CS_NATIVE_TYPE_MAP.get(f['type'])
                if f['is_enum']:
                    enum_cs = to_dotnet_name(f['enum_c_name'])
                    lines.append(f"{indent}result.{cs_field} = ({enum_cs}){read_expr};")
                elif f['type'] == 'bool':
                    lines.append(f"{indent}result.{cs_field} = {read_expr} != 0;")
                elif native:
                    lines.append(f"{indent}result.{cs_field} = ({native}){read_expr};")
                else:
                    lines.append(f"{indent}result.{cs_field} = {read_expr};")
                lines.append(f"{indent}offset += {sz};")

    lines.append(f"{indent}bytesRead = offset;")
    lines.append(f"{indent}return true;")
    return lines


def gen_span_write_core(cs_struct_name: str, fields: list, structs: dict,
                        big_endian: bool, indent: str = "        ",
                        fixed_mode: bool = True) -> list:
    nested_suffix = ""  # nested struct calls are always unsuffixed
    bp_write = bp_write_be if big_endian else bp_write_le
    bp_read_fn = bp_read_be if big_endian else bp_read_le  # not used but kept for symmetry
    all_struct_names = set(structs.keys())
    lines = []
    lines.append(f"{indent}bytesWritten = 0;")
    lines.append(f"{indent}int offset = 0;")

    for f in fields:
        cs_field = f['cs_name']
        wt = f['wire_type']
        sz = WIRE_TYPE_SIZES.get(wt, 0)
        arr = f['array_len']
        is_struct = wt in all_struct_names
        inner  = indent + "    "
        inner2 = inner  + "    "

        if arr is not None and is_char_array(f):
            if fixed_mode:
                # UTF-8 string: encode and write exactly arr bytes, zero-padded
                lines.append(f"{indent}if (span.Length - offset < {arr}) return false;")
                lines.append(f"{indent}Buffers.EncodeUtf8({cs_field}, span.Slice(offset, {arr}));")
                lines.append(f"{indent}offset += {arr};")
            else:
                # Variable: compute UTF-8 byte length, write prefix, then bytes
                lp_wt = length_prefix_type(arr)
                lp_sz = WIRE_TYPE_SIZES[lp_wt]
                lp_cs = cs_wire_type(lp_wt)
                lines.append(f"{indent}{{")
                lines.append(f"{inner}int _byteLen_{cs_field} = string.IsNullOrEmpty({cs_field}) ? 0 : Encoding.UTF8.GetByteCount({cs_field});")
                lines.append(f"{inner}if (_byteLen_{cs_field} > {arr}) _byteLen_{cs_field} = {arr};")
                lines.append(f"{inner}if (span.Length - offset < {lp_sz} + _byteLen_{cs_field}) return false;")
                write_len = bp_write(lp_wt, "span", "offset", f"({lp_cs})_byteLen_{cs_field}")
                lines.append(f"{inner}{write_len}")
                lines.append(f"{inner}offset += {lp_sz};")
                lines.append(f"{inner}if (_byteLen_{cs_field} > 0)")
                lines.append(f"{inner}{{")
                lines.append(f"{inner2}Encoding.UTF8.GetBytes({cs_field}.AsSpan(), span.Slice(offset, _byteLen_{cs_field}));")
                lines.append(f"{inner2}offset += _byteLen_{cs_field};")
                lines.append(f"{inner}}}")
                lines.append(f"{indent}}}")

        elif arr is not None and is_wchar_array(f):
            if fixed_mode:
                # UTF-16LE string
                byte_count = arr * 2
                lines.append(f"{indent}if (span.Length - offset < {byte_count}) return false;")
                lines.append(f"{indent}Buffers.EncodeUtf16LE({cs_field}, span.Slice(offset, {byte_count}));")
                lines.append(f"{indent}offset += {byte_count};")
            else:
                # Variable: length prefix is element count (wchar_t), each 2 bytes
                lp_wt = length_prefix_type(arr)
                lp_sz = WIRE_TYPE_SIZES[lp_wt]
                lp_cs = cs_wire_type(lp_wt)
                lines.append(f"{indent}{{")
                lines.append(f"{inner}int _charLen_{cs_field} = string.IsNullOrEmpty({cs_field}) ? 0 : Math.Min({cs_field}.Length, {arr});")
                lines.append(f"{inner}int _byteLen_{cs_field} = _charLen_{cs_field} * 2;")
                lines.append(f"{inner}if (span.Length - offset < {lp_sz} + _byteLen_{cs_field}) return false;")
                write_len = bp_write(lp_wt, "span", "offset", f"({lp_cs})_charLen_{cs_field}")
                lines.append(f"{inner}{write_len}")
                lines.append(f"{inner}offset += {lp_sz};")
                lines.append(f"{inner}if (_byteLen_{cs_field} > 0)")
                lines.append(f"{inner}{{")
                lines.append(f"{inner2}Encoding.Unicode.GetBytes({cs_field}.AsSpan(0, _charLen_{cs_field}), span.Slice(offset, _byteLen_{cs_field}));")
                lines.append(f"{inner2}offset += _byteLen_{cs_field};")
                lines.append(f"{inner}}}")
                lines.append(f"{indent}}}")

        elif arr is not None and is_struct_array(f, all_struct_names):
            # IList<T>: always write all declared elements (no length prefix)
            nested_cs = to_dotnet_name(wt)
            lines.append(f"{indent}{{")
            lines.append(f"{inner}int _count_{cs_field} = {cs_field} != null ? Math.Min({cs_field}.Count, {arr}) : 0;")
            lines.append(f"{inner}for (int i = 0; i < _count_{cs_field}; i++)")
            lines.append(f"{inner}{{")
            lines.append(f"{inner2}var _item_{cs_field} = {cs_field}[i];")
            lines.append(f"{inner2}if (!_item_{cs_field}.TryWrite{nested_suffix}(span.Slice(offset), out int _w_{cs_field})) return false;")
            lines.append(f"{inner2}offset += _w_{cs_field};")
            lines.append(f"{inner}}}")
            # zero-fill any un-written slots
            nested_wire_sz = struct_wire_size(wt, structs, fixed_mode=fixed_mode)
            lines.append(f"{inner}for (int i = _count_{cs_field}; i < {arr}; i++)")
            lines.append(f"{inner}{{")
            lines.append(f"{inner2}span.Slice(offset, {nested_wire_sz}).Clear();")
            lines.append(f"{inner2}offset += {nested_wire_sz};")
            lines.append(f"{inner}}}")
            lines.append(f"{indent}}}")

        elif arr is not None:
            # scalar or enum array: always write all declared elements (no length prefix)
            cs_t = cs_wire_type(wt)
            is_enum = f['is_enum']
            lines.append(f"{indent}{{")
            lines.append(f"{inner}int _count_{cs_field} = {cs_field} != null ? Math.Min({cs_field}.Length, {arr}) : 0;")
            lines.append(f"{inner}for (int i = 0; i < _count_{cs_field}; i++)")
            lines.append(f"{inner}{{")
            lines.append(f"{inner2}if (span.Length - offset < {sz}) return false;")
            if is_enum:
                val_expr = f"({cs_t}){cs_field}[i]"
            elif f['type'] == 'bool':
                val_expr = f"(byte)({cs_field}[i] ? 1 : 0)"
            elif f['type'] in CS_NATIVE_TYPE_MAP:
                val_expr = f"({cs_t}){cs_field}[i]"
            else:
                val_expr = f"{cs_field}[i]"
            write_stmt = bp_write(wt, "span", "offset", val_expr)
            lines.append(f"{inner2}{write_stmt}")
            lines.append(f"{inner2}offset += {sz};")
            lines.append(f"{inner}}}")
            # zero-fill remaining slots
            lines.append(f"{inner}for (int i = _count_{cs_field}; i < {arr}; i++)")
            lines.append(f"{inner}{{")
            lines.append(f"{inner2}if (span.Length - offset < {sz}) return false;")
            zero_stmt = bp_write(wt, "span", "offset", f"({cs_t})0")
            lines.append(f"{inner2}{zero_stmt}")
            lines.append(f"{inner2}offset += {sz};")
            lines.append(f"{inner}}}")
            lines.append(f"{indent}}}")

        else:
            # single field
            if is_struct:
                nested_cs = to_dotnet_name(wt)
                lines.append(f"{indent}if ({cs_field} == null) return false;")
                lines.append(f"{indent}if (!{cs_field}.TryWrite{nested_suffix}(span.Slice(offset), out int _w_{cs_field})) return false;")
                lines.append(f"{indent}offset += _w_{cs_field};")
            else:
                lines.append(f"{indent}if (span.Length - offset < {sz}) return false;")
                cs_t = cs_wire_type(wt)
                if f['is_enum']:
                    val_expr = f"({cs_t}){cs_field}"
                elif f['type'] == 'bool':
                    val_expr = f"(byte)({cs_field} ? 1 : 0)"
                elif f['type'] in CS_NATIVE_TYPE_MAP:
                    val_expr = f"({cs_t}){cs_field}"
                else:
                    val_expr = f"{cs_field}"
                write_stmt = bp_write(wt, "span", "offset", val_expr)
                lines.append(f"{indent}{write_stmt}")
                lines.append(f"{indent}offset += {sz};")

    lines.append(f"{indent}bytesWritten = offset;")
    lines.append(f"{indent}return true;")
    return lines


# ---------------------------------------------------------------------------
# Computed SizeOfStruct property body (variable-length mode only)
# ---------------------------------------------------------------------------

def gen_size_of_struct_body(fields: list, structs: dict, prefix: str = "            ") -> list:
    """Generate the body of the SizeOfStruct property getter that computes
    the actual wire size of this instance."""
    all_struct_names = set(structs.keys())
    lines = []
    lines.append(f"{prefix}int size = 0;")

    for f in fields:
        cs_field = f['cs_name']
        wt = f['wire_type']
        sz = WIRE_TYPE_SIZES.get(wt, 0)
        arr = f['array_len']
        is_struct = wt in all_struct_names

        if arr is not None:
            if is_char_array(f):
                # string: prefix + UTF-8 byte count
                lp_wt = length_prefix_type(arr)
                lp_sz = WIRE_TYPE_SIZES[lp_wt]
                lines.append(f"{prefix}size += {lp_sz} + (string.IsNullOrEmpty({cs_field}) ? 0 : Math.Min(Encoding.UTF8.GetByteCount({cs_field}), {arr}));")
            elif is_wchar_array(f):
                # wchar string: prefix + char count * 2
                lp_wt = length_prefix_type(arr)
                lp_sz = WIRE_TYPE_SIZES[lp_wt]
                lines.append(f"{prefix}size += {lp_sz} + (string.IsNullOrEmpty({cs_field}) ? 0 : Math.Min({cs_field}.Length, {arr}) * 2);")
            elif is_struct_array(f, all_struct_names):
                # struct array: no prefix, sum of nested sizes for all declared elements
                nested_cs = to_dotnet_name(wt)
                lines.append(f"{prefix}{{")
                lines.append(f"{prefix}    if ({cs_field} != null)")
                lines.append(f"{prefix}        for (int i = 0; i < Math.Min({cs_field}.Count, {arr}); i++)")
                lines.append(f"{prefix}            size += {cs_field}[i].SizeOfStruct;")
                lines.append(f"{prefix}}}")
            else:
                # scalar/enum array: no prefix, full declared count * element size
                lines.append(f"{prefix}size += {arr} * {sz};")
        else:
            if is_struct:
                nested_cs = to_dotnet_name(wt)
                lines.append(f"{prefix}size += {cs_field} != null ? {cs_field}.SizeOfStruct : 0;")
            else:
                lines.append(f"{prefix}size += {sz};")

    lines.append(f"{prefix}return size;")
    return lines


# ---------------------------------------------------------------------------
# Per-struct C# class generation
# ---------------------------------------------------------------------------

def gen_struct_cs(struct_name: str, info: dict, structs: dict,
                  enums: dict, type_vis: str, member_vis: str, wire_size: int,
                  fixed_mode: bool = True, big_endian: bool = False) -> str:
    cs_name = info['cs_name']
    fields = info['fields']
    lines = []

    lines.append(f"{type_vis}partial class {cs_name}")
    lines.append("{")

    # Max size constant (always present)
    lines.append(f"    {member_vis}const int StructMaxSize = {struct_wire_size(struct_name,structs, fixed_mode=fixed_mode)};")
    lines.append("")

    if not fields:
        # Zero-field struct: no Core methods needed, emit minimal no-op public methods

        # Computed instance SizeOfStruct property (only in variable-length mode)
        if not fixed_mode:
            lines.append(f"    {member_vis}int SizeOfStruct => 0;")
            lines.append("")

        # Public Span overloads - no-op implementations (single endianness, no suffix)
        lines.append(f"    {member_vis}static bool TryRead(ReadOnlySpan<byte> span, out {cs_name} result, out int bytesRead)")
        lines.append("    {")
        lines.append(f"        result = new {cs_name}();")
        lines.append("        bytesRead = 0;")
        lines.append("        return true;")
        lines.append("    }")
        lines.append("")

        lines.append(f"    {member_vis}bool TryWrite(Span<byte> destination, out int bytesWritten)")
        lines.append("    {")
        lines.append("        bytesWritten = 0;")
        lines.append("        return true;")
        lines.append("    }")
        lines.append("")

        # Stream overloads - no-op implementations (single endianness, no suffix)
        lines.append(f"    {member_vis}static bool TryRead(Stream stream, out {cs_name} result, out int bytesRead)")
        lines.append("    {")
        lines.append(f"        result = new {cs_name}();")
        lines.append("        bytesRead = 0;")
        lines.append("        return true;")
        lines.append("    }")
        lines.append("")

        lines.append(f"    {member_vis}bool TryWrite(Stream stream, out int bytesWritten)")
        lines.append("    {")
        lines.append("        bytesWritten = 0;")
        lines.append("        return true;")
        lines.append("    }")
        lines.append("")

        lines.append("}")
        return "\n".join(lines)

    lines.extend(gen_field_declarations(fields, structs, member_vis))
    lines.append("")

    # Computed instance SizeOfStruct property (only in variable-length mode)
    if not fixed_mode:
        lines.append(f"    {member_vis}int SizeOfStruct")
        lines.append("    {")
        lines.append("        get")
        lines.append("        {")
        lines.extend(gen_size_of_struct_body(fields, structs, prefix="            "))
        lines.append("        }")
        lines.append("    }")
        lines.append("")

    # Private span cores (single endianness, no suffix on method names)
    lines.append(f"    private static bool TryReadCore(ReadOnlySpan<byte> span, out {cs_name} result, out int bytesRead)")
    lines.append("    {")
    lines.extend(gen_span_read_core(cs_name, fields, structs, big_endian, fixed_mode=fixed_mode))
    lines.append("    }")
    lines.append("")

    lines.append(f"    private bool TryWriteCore(Span<byte> span, out int bytesWritten)")
    lines.append("    {")
    lines.extend(gen_span_write_core(cs_name, fields, structs, big_endian, fixed_mode=fixed_mode))
    lines.append("    }")
    lines.append("")

    # Public Span overloads (single endianness, no suffix)
    lines.append(f"    {member_vis}static bool TryRead(ReadOnlySpan<byte> span, out {cs_name} result, out int bytesRead)")
    lines.append("    {")
    lines.append(f"        return TryReadCore(span, out result, out bytesRead);")
    lines.append("    }")
    lines.append("")

    lines.append(f"    {member_vis}bool TryWrite(Span<byte> destination, out int bytesWritten)")
    lines.append("    {")
    lines.append(f"        return TryWriteCore(destination, out bytesWritten);")
    lines.append("    }")
    lines.append("")

    # Stream overloads (single endianness, no suffix)
    lines.append(f"    {member_vis}static bool TryRead(Stream stream, out {cs_name} result, out int bytesRead)")
    lines.append("    {")
    lines.append(f"        Span<byte> buf = stackalloc byte[{wire_size}];")
    lines.append(f"        int n = stream.Read(buf);")
    lines.append(f"        if (n < {wire_size}) {{ result = null; bytesRead = n; return false; }}")
    lines.append(f"        return TryReadCore(buf, out result, out bytesRead);")
    lines.append("    }")
    lines.append("")

    lines.append(f"    {member_vis}bool TryWrite(Stream stream, out int bytesWritten)")
    lines.append("    {")
    lines.append(f"        Span<byte> buf = stackalloc byte[{wire_size}];")
    lines.append(f"        if (!TryWriteCore(buf, out bytesWritten)) return false;")
    lines.append(f"        stream.Write(buf.Slice(0, bytesWritten));")
    lines.append(f"        return true;")
    lines.append("    }")
    lines.append("")

    lines.append("}")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Enum C# generation
# ---------------------------------------------------------------------------

def gen_enum_cs(c_name: str, info: dict, type_vis: str) -> str:
    cs_name = to_dotnet_name(c_name)
    wire_type = info['wire_type']
    cs_backing = cs_wire_type(wire_type)
    lines = []
    lines.append(f"{type_vis}enum {cs_name} : {cs_backing}")
    lines.append("{")
    for cs_member, value in info['members']:
        lines.append(f"    {cs_member} = {value},")
    lines.append("}")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# MaxSize struct generation
# ---------------------------------------------------------------------------

def gen_maxsize_cs(header_stem: str, max_size: int, type_vis: str, member_vis: str) -> str:
    cs_stem = to_dotnet_name(header_stem)
    cs_name = f"{cs_stem}MaxSize"
    lines = []
    lines.append(f"{type_vis}struct {cs_name}")
    lines.append("{")
    lines.append(f"    {member_vis}const int Value = {max_size};")
    lines.append("}")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Top-level file assembly
# ---------------------------------------------------------------------------

def generate_cs_file(header_path: str, structs: dict, enums: dict,
                     namespace: str, is_public: bool, fixed_mode: bool = True,
                     big_endian: bool = False) -> str:
    type_vis   = "public " if is_public else ""
    member_vis = "public " if is_public else "internal "
    stem = os.path.splitext(os.path.basename(header_path))[0]
    max_size = compute_max_wire_size(structs, fixed_mode=fixed_mode)

    lines = []
    lines.append("// <auto-generated />")
    lines.append("// Auto-generated by buffers_gen_cs.py - do not edit manually.")
    lines.append("#nullable disable")
    lines.append("using System;")
    lines.append("using System.Buffers.Binary;")
    lines.append("using System.Collections.Generic;")
    lines.append("using System.IO;")
    lines.append("using System.Text;")
    lines.append("using Htcw;")
    lines.append("")

    if namespace:
        lines.append(f"namespace {namespace};")
        lines.append("")

    # Enums first
    for c_name, info in enums.items():
        lines.append(gen_enum_cs(c_name, info, type_vis))
        lines.append("")

    # MaxSize struct
    lines.append(gen_maxsize_cs(stem, max_size, type_vis, member_vis))
    lines.append("")

    # Structs
    for struct_name, info in structs.items():
        wire_size = struct_wire_size(struct_name, structs, fixed_mode=fixed_mode)
        lines.append(gen_struct_cs(struct_name, info, structs, enums, type_vis, member_vis, wire_size, fixed_mode=fixed_mode, big_endian=big_endian))
        lines.append("")

    lines.append("#nullable restore")
    return "\n".join(lines)


def generate_buffers_cs() -> str:
    return """\
// Auto-generated by buffers_gen_cs.py - do not edit manually.
// Shared support types for wire buffer reading/writing.
#nullable disable
using System;
using System.Text;

namespace Htcw;

/// <summary>Status codes mirroring the C buffers.h enum.</summary>
internal static class BuffersStatus
{
    public const int Success  =  0;
    public const int Eof      = -1;
    public const int ErrorEof = -2;
}

/// <summary>Wire string encode/decode helpers used by generated code.</summary>
internal static class Buffers
{
    /// <summary>
    /// Decode a fixed-length UTF-8 byte field into a string.
    /// Strips everything at and after the first null byte.
    /// </summary>
    internal static string DecodeUtf8(ReadOnlySpan<byte> span)
    {
        int len = span.IndexOf((byte)0);
        if (len < 0) len = span.Length;
        return Encoding.UTF8.GetString(span.Slice(0, len));
    }

    /// <summary>
    /// Decode a fixed-length UTF-16LE byte field into a string.
    /// Strips everything at and after the first null char (0x0000).
    /// </summary>
    internal static string DecodeUtf16LE(ReadOnlySpan<byte> span)
    {
        // Find first null char (two zero bytes aligned on even boundary)
        int len = span.Length & ~1; // round down to even
        for (int i = 0; i < len; i += 2)
        {
            if (span[i] == 0 && span[i + 1] == 0) { len = i; break; }
        }
        return Encoding.Unicode.GetString(span.Slice(0, len));
    }

    /// <summary>
    /// Encode a string as UTF-8 into a fixed-length span, zero-padded.
    /// Uses Encoder.Convert to guarantee truncation on a valid code-point
    /// boundary - no partial sequences are ever written.
    /// </summary>
    internal static void EncodeUtf8(string value, Span<byte> dest)
    {
        dest.Clear();
        if (string.IsNullOrEmpty(value)) return;
        var encoder = Encoding.UTF8.GetEncoder();
        var chars = value.AsSpan();
        encoder.Convert(chars, dest, flush: true,
            out int charsUsed, out int bytesUsed, out bool completed);
    }

    /// <summary>
    /// Encode a string as UTF-16LE into a fixed-length span, zero-padded.
    /// Uses Encoder.Convert to guarantee truncation on a valid code-unit
    /// boundary - surrogate pairs are never split.
    /// </summary>
    internal static void EncodeUtf16LE(string value, Span<byte> dest)
    {
        dest.Clear();
        if (string.IsNullOrEmpty(value)) return;
        var encoder = Encoding.Unicode.GetEncoder();
        var chars = value.AsSpan();
        encoder.Convert(chars, dest, flush: true,
            out int charsUsed, out int bytesUsed, out bool completed);
    }
}
#nullable restore
"""


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    args = sys.argv[1:]
    namespace = None
    is_public = False
    gen_buffers = False
    out_dir = ""
    fixed_mode = False  # Default: variable-length (length-prefixed) serialization
    big_endian = False
    while args and args[0].startswith('--'):
        opt = args.pop(0)
        if opt == '--namespace':
            if not args:
                error("--namespace requires an argument")
            namespace = args.pop(0)
        elif opt == '--public':
            is_public = True
        elif opt == '--buffers':
            gen_buffers = True
        elif opt == '--out':
            out_dir = args.pop(0)
        elif opt == '--fixed':
            fixed_mode = True
        elif opt == '--big-endian':
            big_endian = True
        else:
            error(f"Unknown option: {opt}")

    if len(args) != 1:
        print(f"Usage: {sys.argv[0]} [--fixed] [--big-endian] [--namespace <Ns>] [--public] [--buffers] <header.h>",
              file=sys.stderr)
        sys.exit(1)

    path = args[0]
    try:
        with open(path, 'r') as f:
            text = f.read()
    except OSError as e:
        error(f"Cannot open file: {e}")

    structs, enums = parse_header(text)
    if not structs:
        error("No structs found in header")

    stem = os.path.splitext(os.path.basename(path))[0]
    cs_stem = to_dotnet_name(stem)
    
    if len(out_dir)==0:
        out_dir = os.path.dirname(path) or '.'

    cs_path      = os.path.join(out_dir, f"{cs_stem}Buffers.cs")
    
    with open(cs_path, 'w') as f:
        f.write(generate_cs_file(path, structs, enums, namespace, is_public, fixed_mode=fixed_mode, big_endian=big_endian))
    print(f"Written: {cs_path}")
    
    if gen_buffers:
        buffers_path = os.path.join(out_dir, "Buffers.cs")
        with open(buffers_path, 'w') as f:
            f.write(generate_buffers_cs())
        print(f"Written: {buffers_path}")

if __name__ == '__main__':
    main()