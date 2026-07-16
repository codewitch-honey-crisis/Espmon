#!/usr/bin/env python3
"""
buffers_gen_cs.py - Parse wire structs from a C header and generate
                    C# read/write methods for each struct.

Usage: python buffers_gen_cs.py [--fixed] [--big-endian] [--lengths] [--friendly] [--bltable | --reorder] [--namespace <ns>] [--public] [--out <dir>] <header.h>

Options:
  --fixed            Use fixed-size serialization for strings
                    (transmit entire declared size). Without this flag,
                    strings are length-prefixed on the wire.
  --big-endian      Generate struct read/write methods using big-endian
                    serialization. Without this flag, little-endian is used.
  --lengths         Treat any size_t field that immediately precedes a fixed
                    array as the runtime element count for that array. The
                    count is serialized first (as uint32_t on the wire), then
                    only that many array elements follow. The count field is
                    HIDDEN from the C# API: on write the count is taken from
                    the array property's .Length / .Count; on read it is
                    consumed but discarded (the array's length carries it).
                    Strings (char[] / wchar_t[]) are unaffected. Mutually
                    exclusive with --fixed.
  --bltable          Emit [StructLayout(LayoutKind.Sequential, Pack = 1)] on
                    each generated struct (blittable, packed wire layout).
                    Off by default because forced Pack = 1 can misalign fields
                    and slow member access. Mutually exclusive with --reorder.
  --reorder          Emit [StructLayout(LayoutKind.Auto)] so the runtime may
                    reorder fields to reduce padding. Makes the struct
                    non-blittable. Mutually exclusive with --bltable.
  --namespace <ns>   File-scoped namespace for generated code (default: none)
  --public           Emit public visibility (default: implicit internal)
  --out <dir>        Overrides the output directory (default: input directory)
  
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

"""
MIT License

Copyright (c) 2026 honey the codewitch

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
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

# C# keyword type -> CLR (System) type name, used when building inline-array
# helper struct names so they read e.g. "UInt32" instead of "uint". Struct
# element types (already PascalCase) fall through unchanged.
CLR_TYPE_NAMES = {
    'byte':   'Byte',   'sbyte':  'SByte',
    'short':  'Int16',  'ushort': 'UInt16',
    'int':    'Int32',  'uint':   'UInt32',
    'long':   'Int64',  'ulong':  'UInt64',
    'float':  'Single', 'double': 'Double',
    'char':   'Char',   'bool':   'Boolean',
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


def apply_lengths_pairing_cs(structs: dict, struct_name: str) -> None:
    """When --lengths is in effect, pair each `size_t name; T arr[N];` pattern
    so the count drives runtime element count for the array. The count field
    is hidden from the C# API (no property emitted). On read, the count is
    consumed from the wire but discarded; the resulting array's length is
    the count. On write, the count is taken from the array's .Length.

    Strings (char[]/wchar_t[]) keep their existing length-prefix behavior.
    The size_t must immediately precede the array.
    """
    fields = structs[struct_name]['fields']
    for i in range(len(fields) - 1):
        cur = fields[i]
        nxt = fields[i + 1]
        if cur['array_len'] is not None:
            continue
        if cur['is_enum']:
            continue
        if cur['type'] != 'size_t':
            continue
        if nxt['array_len'] is None:
            continue
        # Strings keep their existing length-prefix behavior.
        if nxt['type'] in ('char', 'wchar_t'):
            continue
        nxt['length_field'] = cur['c_name']
        cur['is_length_for'] = nxt['c_name']


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


def parse_header(text: str, lengths_mode: bool = False) -> tuple:
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

    if lengths_mode:
        for name in structs:
            apply_lengths_pairing_cs(structs, name)

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
# Inline-array helper registry
# ---------------------------------------------------------------------------
#
# Each distinct (element C# type, length) pair used by any struct becomes one
# [InlineArray(N)] helper struct, emitted once per file. These give us fixed
# storage embedded directly in the owning struct - no heap array.

def _sanitize_token(cs_type: str) -> str:
    return re.sub(r'[^A-Za-z0-9]', '', cs_type)


def _clr_type_name(cs_type: str) -> str:
    """Map a C# element type to the name used in an inline-array helper's
    identifier: CLR name for primitives (uint -> UInt32), sanitized as-is for
    struct element types (already PascalCase)."""
    return CLR_TYPE_NAMES.get(cs_type, _sanitize_token(cs_type))


def inline_name(reg: dict, order: list, elem_cs: str, n: int,
                prefix: str = "") -> str:
    # Name shape: {HeaderStem}Inline{ClrType}Length{N}
    #   - prefix keeps helpers from colliding across separately generated files
    #   - "Length" separates type from count so UInt32 + 1024 reads as
    #     "UInt32Length1024" rather than the ambiguous "UInt321024"
    key = (elem_cs, n)
    if key not in reg:
        name = f"{prefix}Inline{_clr_type_name(elem_cs)}Length{n}"
        reg[key] = name
        order.append((name, elem_cs, n))
    return reg[key]


def gen_inline_helpers(order: list) -> list:
    lines = []
    for name, elem_cs, n in order:
        lines.append(f"[InlineArray({n})]")
        lines.append(f"struct {name}")
        lines.append("{")
        lines.append(f"    private {elem_cs} _e0;")
        lines.append("}")
        lines.append("")
    return lines


# ---------------------------------------------------------------------------
# Per-field storage / element type helpers
# ---------------------------------------------------------------------------
#
# Storage kinds:
#   - "field"    : a plain public field IS the API (scalars, enums, nested struct)
#   - "backing"  : a private backing field with a friendly property (bool, single
#                  char/wchar_t)
#   - inline     : a private [InlineArray] field + span/friendly accessors
#                  (arrays and strings)

def is_backing_scalar(f: dict) -> bool:
    """Single (non-array) bool / char / wchar_t -> backing field + property."""
    if f['array_len'] is not None:
        return False
    return f['type'] in ('bool', 'char', 'wchar_t')


def span_elem_type(f: dict, structs: dict) -> str:
    """C# element type exposed through the *span* view of an array field."""
    wt = f['wire_type']
    if is_struct_array(f, set(structs.keys())):
        return to_dotnet_name(wt)
    if f['is_enum']:
        return to_dotnet_name(f['enum_c_name'])
    if f['type'] == 'bool':
        return 'byte'
    return cs_wire_type(wt)


def friendly_elem_type(f: dict, structs: dict) -> str:
    """C# element type exposed through the *friendly* (array) view."""
    if f['type'] == 'bool':
        return 'bool'
    return span_elem_type(f, structs)


def storage_elem_type(f: dict, structs: dict) -> str:
    """C# element type actually stored inside the [InlineArray]."""
    # char[N] UTF-8 -> byte storage; wchar_t[N] -> char storage; else span type.
    if is_char_array(f):
        return 'byte'
    if is_wchar_array(f):
        return 'char'
    return span_elem_type(f, structs)


def storage_symbol(f: dict) -> str:
    """The lvalue used in read/write cores to reach a field's storage."""
    if is_backing_scalar(f):
        return f"_{f['c_name']}"
    if f['array_len'] is not None:
        return f"_{f['c_name']}"          # inline array private field
    # plain public field
    return f['cs_name']


# ---------------------------------------------------------------------------
# Field member declarations (storage + accessors)
# ---------------------------------------------------------------------------

def gen_field_members(fields: list, structs: dict, member_vis: str,
                      reg: dict, order: list, friendly: bool,
                      fixed_mode: bool, prefix: str = "") -> list:
    all_struct_names = set(structs.keys())
    lines = []
    UR = "    [UnscopedRef] "

    for f in fields:
        if f.get('is_length_for') is not None:
            continue  # hidden count field: no storage of its own, no accessor

        name = f['cs_name']
        arr = f['array_len']

        # ---- plain scalar / enum single -> public field ------------------
        if arr is None and not is_backing_scalar(f) and f['wire_type'] not in all_struct_names:
            if f['is_enum']:
                cs_t = to_dotnet_name(f['enum_c_name'])
            else:
                cs_t = cs_wire_type(f['wire_type'])
            lines.append(f"    {member_vis}{cs_t} {name};")
            continue

        # ---- nested struct single -> public field ------------------------
        if arr is None and f['wire_type'] in all_struct_names:
            lines.append(f"    {member_vis}{to_dotnet_name(f['wire_type'])} {name};")
            continue

        # ---- bool single -------------------------------------------------
        if arr is None and f['type'] == 'bool':
            lines.append(f"    private byte _{f['c_name']};")
            lines.append(f"    {member_vis}bool {name} {{ get => _{f['c_name']} != 0; set => _{f['c_name']} = value ? (byte)1 : (byte)0; }}")
            continue

        # ---- single char (int8 wire) -------------------------------------
        if arr is None and f['type'] == 'char':
            lines.append(f"    private byte _{f['c_name']};")
            lines.append(f"    {member_vis}char {name} {{ get => (char)_{f['c_name']}; set => _{f['c_name']} = (byte)value; }}")
            continue

        # ---- single wchar_t (int16 wire) ---------------------------------
        if arr is None and f['type'] == 'wchar_t':
            lines.append(f"    private short _{f['c_name']};")
            lines.append(f"    {member_vis}char {name} {{ get => (char)(ushort)_{f['c_name']}; set => _{f['c_name']} = (short)value; }}")
            continue

        # ================= array / string fields ==========================
        stet = storage_elem_type(f, structs)
        inl = inline_name(reg, order, stet, arr, prefix)
        lines.append(f"    private {inl} _{f['c_name']};")

        is_lp = f.get('length_field') is not None
        if is_lp:
            lines.append(f"    private int _{f['c_name']}Count;")

        # ---- UTF-8 string (char[N]) --------------------------------------
        if is_char_array(f):
            get_view = f"Buffers.TrimNullUtf8((ReadOnlySpan<byte>)_{f['c_name']})"
            all_span = f"((Span<byte>)_{f['c_name']})"
            copy_in = (f"{{ if (value.Length > {arr}) throw new ArgumentException(\"value exceeds capacity\", nameof(value)); "
                       f"value.CopyTo({all_span}); if (value.Length < {arr}) {all_span}.Slice(value.Length).Clear(); }}")
            if not friendly:
                lines.append(f"{UR}{member_vis}ReadOnlySpan<byte> {name}")
                lines.append("    {")
                lines.append(f"        get => {get_view};")
                lines.append(f"        set {copy_in}")
                lines.append("    }")
                lines.append(f"    {member_vis}string Get{name}() => Buffers.DecodeUtf8((ReadOnlySpan<byte>)_{f['c_name']});")
                lines.append(f"    {member_vis}void Set{name}(ReadOnlySpan<char> value) => Buffers.EncodeUtf8(value, {all_span});")
                lines.append(f"    {member_vis}void Set{name}(string value) => Buffers.EncodeUtf8(value.AsSpan(), {all_span});")
            else:
                lines.append(f"    {member_vis}string {name}")
                lines.append("    {")
                lines.append(f"        get => Buffers.DecodeUtf8((ReadOnlySpan<byte>)_{f['c_name']});")
                lines.append(f"        set => Buffers.EncodeUtf8(value.AsSpan(), {all_span});")
                lines.append("    }")
                lines.append(f"{UR}{member_vis}ReadOnlySpan<byte> Get{name}Span() => {get_view};")
                lines.append(f"    {member_vis}void Set{name}(ReadOnlySpan<char> value) => Buffers.EncodeUtf8(value, {all_span});")
            continue

        # ---- UTF-16 string (wchar_t[N]) ----------------------------------
        if is_wchar_array(f):
            get_view = f"Buffers.TrimNullUtf16((ReadOnlySpan<char>)_{f['c_name']})"
            all_span = f"((Span<char>)_{f['c_name']})"
            copy_in = (f"{{ int _n = Math.Min(value.Length, {arr}); value.Slice(0, _n).CopyTo({all_span}); "
                       f"if (_n < {arr}) {all_span}.Slice(_n).Clear(); }}")
            if not friendly:
                lines.append(f"{UR}{member_vis}ReadOnlySpan<char> {name}")
                lines.append("    {")
                lines.append(f"        get => {get_view};")
                lines.append(f"        set {copy_in}")
                lines.append("    }")
                lines.append(f"    {member_vis}string Get{name}() => new string({get_view});")
                lines.append(f"    {member_vis}void Set{name}(string value) => {name} = value.AsSpan();")
            else:
                lines.append(f"    {member_vis}string {name}")
                lines.append("    {")
                lines.append(f"        get => new string({get_view});")
                lines.append(f"        set {{ ReadOnlySpan<char> _v = value.AsSpan(); int _n = Math.Min(_v.Length, {arr}); _v.Slice(0, _n).CopyTo({all_span}); if (_n < {arr}) {all_span}.Slice(_n).Clear(); }}")
                lines.append("    }")
                lines.append(f"{UR}{member_vis}ReadOnlySpan<char> Get{name}Span() => {get_view};")
            continue

        # ---- scalar / enum / struct array (fixed or length-prefixed) -----
        # Getters expose a ReadOnlySpan (a read view into this instance's own
        # inline storage). They are intentionally NOT mutable spans: a mutable
        # span returned from a value type silently aliases whichever copy of the
        # struct produced it, so `list[i].Foo[0] = x` would mutate a throwaway
        # copy. Writes go exclusively through the (copying) setter.
        set_t = span_elem_type(f, structs)
        fr_t = friendly_elem_type(f, structs)
        wr_span = f"((Span<{set_t}>)_{f['c_name']})"          # writable, setter-only
        ro_all = f"((ReadOnlySpan<{set_t}>)_{f['c_name']})"   # read view (full capacity)
        if is_lp:
            ro_view = f"{ro_all}.Slice(0, _{f['c_name']}Count)"
        else:
            ro_view = ro_all

        # setter body (copies the incoming read-only span into storage)
        if is_lp:
            set_body = (f"{{ if (value.Length > {arr}) throw new ArgumentException(\"value exceeds capacity\", nameof(value)); "
                        f"value.CopyTo({wr_span}); _{f['c_name']}Count = value.Length; }}")
        else:
            set_body = (f"{{ if (value.Length > {arr}) throw new ArgumentException(\"value exceeds capacity\", nameof(value)); "
                        f"value.CopyTo({wr_span}); if (value.Length < {arr}) {wr_span}.Slice(value.Length).Clear(); }}")

        # friendly getter/setter expressions (bool needs elementwise convert)
        if f['type'] == 'bool':
            fr_get = f"Buffers.ToBoolArray({ro_view})"
            fr_set_body = f"{{ Buffers.FromBoolArray(value, {wr_span}{(', out _' + f['c_name'] + 'Count') if is_lp else ''}{(', ' + str(arr)) if not is_lp else ''}); }}"
        else:
            fr_get = f"{ro_view}.ToArray()"
            fr_set_body = set_body  # T[] converts implicitly to ReadOnlySpan<T>

        if not friendly:
            lines.append(f"{UR}{member_vis}ReadOnlySpan<{set_t}> {name}")
            lines.append("    {")
            lines.append(f"        get => {ro_view};")
            lines.append(f"        set {set_body}")
            lines.append("    }")
            lines.append(f"    {member_vis}{fr_t}[] Get{name}() => {fr_get};")
        else:
            lines.append(f"    {member_vis}{fr_t}[] {name}")
            lines.append("    {")
            lines.append(f"        get => {fr_get};")
            lines.append(f"        set {fr_set_body}")
            lines.append("    }")
            lines.append(f"{UR}{member_vis}ReadOnlySpan<{set_t}> Get{name}Span() => {ro_view};")

    return lines


# ---------------------------------------------------------------------------
# Blittable fast-path (fixed + non-big-endian only)
# ---------------------------------------------------------------------------

def _fast_path_eligible(fields, fixed_mode, big_endian):
    return bool(fields) and fixed_mode and not big_endian


# ---------------------------------------------------------------------------
# Span read core  (fills inline storage of `result`)
# ---------------------------------------------------------------------------

def gen_span_read_core(cs_name, fields, structs, big_endian,
                       fixed_mode=True, indent="        "):
    bp_read = bp_read_be if big_endian else bp_read_le
    all_struct_names = set(structs.keys())
    L = lines = []
    inner = indent + "    "
    inner2 = inner + "    "

    lines.append(f"{indent}result = default;")
    if _fast_path_eligible(fields, fixed_mode, big_endian):
        lines.append(f"{indent}if (BitConverter.IsLittleEndian && Unsafe.SizeOf<{cs_name}>() == StructMaxSize)")
        lines.append(f"{indent}{{")
        lines.append(f"{inner}if (span.Length < StructMaxSize) {{ bytesRead = 0; return false; }}")
        lines.append(f"{inner}result = MemoryMarshal.Read<{cs_name}>(span);")
        lines.append(f"{inner}bytesRead = StructMaxSize;")
        lines.append(f"{inner}return true;")
        lines.append(f"{indent}}}")
    lines.append(f"{indent}int offset = 0;")

    for f in fields:
        if f.get('is_length_for') is not None:
            continue
        name = f['cs_name']
        wt = f['wire_type']
        sz = WIRE_TYPE_SIZES.get(wt, 0)
        arr = f['array_len']
        is_struct = wt in all_struct_names
        stg = f"result._{f['c_name']}"

        # ----- length-prefixed array -----
        if arr is not None and f.get('length_field') is not None:
            read_count = bp_read('uint32_t', "span", "offset")
            lines.append(f"{indent}{{")
            lines.append(f"{inner}if (span.Length - offset < 4) {{ bytesRead = 0; return false; }}")
            lines.append(f"{inner}int _c = (int)({read_count}); offset += 4;")
            lines.append(f"{inner}if ((uint)_c > {arr}) {{ bytesRead = 0; return false; }}")
            if is_struct_array(f, all_struct_names):
                nested = to_dotnet_name(wt)
                lines.append(f"{inner}for (int i = 0; i < _c; i++)")
                lines.append(f"{inner}{{")
                lines.append(f"{inner2}if (!{nested}.TryReadCore(span.Slice(offset), out var _it, out int _n)) {{ bytesRead = 0; return false; }}")
                lines.append(f"{inner2}{stg}[i] = _it; offset += _n;")
                lines.append(f"{inner}}}")
            else:
                lines.append(f"{inner}for (int i = 0; i < _c; i++)")
                lines.append(f"{inner}{{")
                lines.append(f"{inner2}if (span.Length - offset < {sz}) {{ bytesRead = 0; return false; }}")
                rd = bp_read(wt, "span", "offset")
                if f['is_enum']:
                    lines.append(f"{inner2}{stg}[i] = ({to_dotnet_name(f['enum_c_name'])})({rd});")
                else:
                    lines.append(f"{inner2}{stg}[i] = {rd};")
                lines.append(f"{inner2}offset += {sz};")
                lines.append(f"{inner}}}")
            lines.append(f"{inner}result._{f['c_name']}Count = _c;")
            lines.append(f"{indent}}}")
            continue

        # ----- UTF-8 string -----
        if arr is not None and is_char_array(f):
            if fixed_mode:
                lines.append(f"{indent}if (span.Length - offset < {arr}) {{ bytesRead = 0; return false; }}")
                lines.append(f"{indent}span.Slice(offset, {arr}).CopyTo((Span<byte>){stg}); offset += {arr};")
            else:
                lp_wt = length_prefix_type(arr); lp_sz = WIRE_TYPE_SIZES[lp_wt]
                rd = bp_read(lp_wt, "span", "offset")
                lines.append(f"{indent}{{")
                lines.append(f"{inner}if (span.Length - offset < {lp_sz}) {{ bytesRead = 0; return false; }}")
                lines.append(f"{inner}int _l = (int)({rd}); offset += {lp_sz};")
                lines.append(f"{inner}if (_l > {arr} || span.Length - offset < _l) {{ bytesRead = 0; return false; }}")
                lines.append(f"{inner}span.Slice(offset, _l).CopyTo((Span<byte>){stg});")
                lines.append(f"{inner}((Span<byte>){stg}).Slice(_l).Clear(); offset += _l;")
                lines.append(f"{indent}}}")
            continue

        # ----- UTF-16 string -----
        if arr is not None and is_wchar_array(f):
            if fixed_mode:
                lines.append(f"{indent}if (span.Length - offset < {arr * 2}) {{ bytesRead = 0; return false; }}")
                lines.append(f"{indent}for (int i = 0; i < {arr}; i++) {{ {stg}[i] = (char)({bp_read('int16_t','span','offset')}); offset += 2; }}")
            else:
                lp_wt = length_prefix_type(arr); lp_sz = WIRE_TYPE_SIZES[lp_wt]
                rd = bp_read(lp_wt, "span", "offset")
                lines.append(f"{indent}{{")
                lines.append(f"{inner}if (span.Length - offset < {lp_sz}) {{ bytesRead = 0; return false; }}")
                lines.append(f"{inner}int _l = (int)({rd}); offset += {lp_sz};")
                lines.append(f"{inner}if (_l > {arr} || span.Length - offset < _l * 2) {{ bytesRead = 0; return false; }}")
                lines.append(f"{inner}for (int i = 0; i < _l; i++) {{ {stg}[i] = (char)({bp_read('int16_t','span','offset')}); offset += 2; }}")
                lines.append(f"{inner}((Span<char>){stg}).Slice(_l).Clear();")
                lines.append(f"{indent}}}")
            continue

        # ----- fixed struct array -----
        if arr is not None and is_struct_array(f, all_struct_names):
            nested = to_dotnet_name(wt)
            lines.append(f"{indent}for (int i = 0; i < {arr}; i++)")
            lines.append(f"{indent}{{")
            lines.append(f"{inner}if (!{nested}.TryReadCore(span.Slice(offset), out var _it, out int _n)) {{ bytesRead = 0; return false; }}")
            lines.append(f"{inner}{stg}[i] = _it; offset += _n;")
            lines.append(f"{indent}}}")
            continue

        # ----- fixed scalar / enum / bool array -----
        if arr is not None:
            lines.append(f"{indent}for (int i = 0; i < {arr}; i++)")
            lines.append(f"{indent}{{")
            lines.append(f"{inner}if (span.Length - offset < {sz}) {{ bytesRead = 0; return false; }}")
            rd = bp_read(wt, "span", "offset")
            if f['is_enum']:
                lines.append(f"{inner}{stg}[i] = ({to_dotnet_name(f['enum_c_name'])})({rd});")
            else:
                lines.append(f"{inner}{stg}[i] = {rd};")
            lines.append(f"{inner}offset += {sz};")
            lines.append(f"{indent}}}")
            continue

        # ----- single nested struct -----
        if is_struct:
            nested = to_dotnet_name(wt)
            lines.append(f"{indent}if (!{nested}.TryReadCore(span.Slice(offset), out result.{name}, out int _n_{f['c_name']})) {{ bytesRead = 0; return false; }}")
            lines.append(f"{indent}offset += _n_{f['c_name']};")
            continue

        # ----- single scalar / enum / bool / char / wchar -----
        lines.append(f"{indent}if (span.Length - offset < {sz}) {{ bytesRead = 0; return false; }}")
        rd = bp_read(wt, "span", "offset")
        if f['is_enum']:
            lines.append(f"{indent}result.{name} = ({to_dotnet_name(f['enum_c_name'])})({rd});")
        elif f['type'] == 'bool':
            lines.append(f"{indent}result._{f['c_name']} = (byte)({rd});")
        elif f['type'] == 'char':
            lines.append(f"{indent}result._{f['c_name']} = (byte)({rd});")
        elif f['type'] == 'wchar_t':
            lines.append(f"{indent}result._{f['c_name']} = (short)({rd});")
        else:
            lines.append(f"{indent}result.{name} = {rd};")
        lines.append(f"{indent}offset += {sz};")

    lines.append(f"{indent}bytesRead = offset;")
    lines.append(f"{indent}return true;")
    return lines


# ---------------------------------------------------------------------------
# Span write core  (reads inline storage of `this`)
# ---------------------------------------------------------------------------

def gen_span_write_core(cs_name, fields, structs, big_endian,
                        fixed_mode=True, indent="        "):
    bp_write = bp_write_be if big_endian else bp_write_le
    all_struct_names = set(structs.keys())
    lines = []
    inner = indent + "    "
    inner2 = inner + "    "

    if _fast_path_eligible(fields, fixed_mode, big_endian):
        lines.append(f"{indent}if (BitConverter.IsLittleEndian && Unsafe.SizeOf<{cs_name}>() == StructMaxSize)")
        lines.append(f"{indent}{{")
        lines.append(f"{inner}if (span.Length < StructMaxSize) {{ bytesWritten = 0; return false; }}")
        lines.append(f"{inner}var _self = this;")
        lines.append(f"{inner}MemoryMarshal.Write(span, in _self);")
        lines.append(f"{inner}bytesWritten = StructMaxSize;")
        lines.append(f"{inner}return true;")
        lines.append(f"{indent}}}")
    lines.append(f"{indent}int offset = 0;")

    for f in fields:
        if f.get('is_length_for') is not None:
            continue
        name = f['cs_name']
        wt = f['wire_type']
        sz = WIRE_TYPE_SIZES.get(wt, 0)
        arr = f['array_len']
        is_struct = wt in all_struct_names
        stg = f"_{f['c_name']}"

        # ----- length-prefixed array -----
        if arr is not None and f.get('length_field') is not None:
            lines.append(f"{indent}{{")
            lines.append(f"{inner}int _c = {stg}Count;")
            lines.append(f"{inner}if ((uint)_c > {arr}) {{ bytesWritten = 0; return false; }}")
            lines.append(f"{inner}if (span.Length - offset < 4) {{ bytesWritten = 0; return false; }}")
            lines.append(f"{inner}{bp_write('uint32_t','span','offset','(uint)_c')} offset += 4;")
            if is_struct_array(f, all_struct_names):
                nested = to_dotnet_name(wt)
                lines.append(f"{inner}for (int i = 0; i < _c; i++)")
                lines.append(f"{inner}{{")
                lines.append(f"{inner2}var _it = {stg}[i];")
                lines.append(f"{inner2}if (!_it.TryWriteCore(span.Slice(offset), out int _n)) {{ bytesWritten = 0; return false; }}")
                lines.append(f"{inner2}offset += _n;")
                lines.append(f"{inner}}}")
            else:
                lines.append(f"{inner}for (int i = 0; i < _c; i++)")
                lines.append(f"{inner}{{")
                lines.append(f"{inner2}if (span.Length - offset < {sz}) {{ bytesWritten = 0; return false; }}")
                val = f"({cs_wire_type(wt)}){stg}[i]" if f['is_enum'] else f"{stg}[i]"
                lines.append(f"{inner2}{bp_write(wt,'span','offset',val)} offset += {sz};")
                lines.append(f"{inner}}}")
            lines.append(f"{indent}}}")
            continue

        # ----- UTF-8 string -----
        if arr is not None and is_char_array(f):
            if fixed_mode:
                lines.append(f"{indent}if (span.Length - offset < {arr}) {{ bytesWritten = 0; return false; }}")
                lines.append(f"{indent}((ReadOnlySpan<byte>){stg}).Slice(0, {arr}).CopyTo(span.Slice(offset, {arr})); offset += {arr};")
            else:
                lp_wt = length_prefix_type(arr); lp_sz = WIRE_TYPE_SIZES[lp_wt]
                lines.append(f"{indent}{{")
                lines.append(f"{inner}int _l = Buffers.TrimNullUtf8((ReadOnlySpan<byte>){stg}).Length;")
                lines.append(f"{inner}if (span.Length - offset < {lp_sz} + _l) {{ bytesWritten = 0; return false; }}")
                lines.append(f"{inner}{bp_write(lp_wt,'span','offset',f'({cs_wire_type(lp_wt)})_l')} offset += {lp_sz};")
                lines.append(f"{inner}((ReadOnlySpan<byte>){stg}).Slice(0, _l).CopyTo(span.Slice(offset, _l)); offset += _l;")
                lines.append(f"{indent}}}")
            continue

        # ----- UTF-16 string -----
        if arr is not None and is_wchar_array(f):
            if fixed_mode:
                lines.append(f"{indent}if (span.Length - offset < {arr * 2}) {{ bytesWritten = 0; return false; }}")
                lines.append(f"{indent}for (int i = 0; i < {arr}; i++) {{ {bp_write('int16_t','span','offset',f'(short)((ReadOnlySpan<char>){stg})[i]')} offset += 2; }}")
            else:
                lp_wt = length_prefix_type(arr); lp_sz = WIRE_TYPE_SIZES[lp_wt]
                lines.append(f"{indent}{{")
                lines.append(f"{inner}int _l = Buffers.TrimNullUtf16((ReadOnlySpan<char>){stg}).Length;")
                lines.append(f"{inner}if (span.Length - offset < {lp_sz} + _l * 2) {{ bytesWritten = 0; return false; }}")
                lines.append(f"{inner}{bp_write(lp_wt,'span','offset',f'({cs_wire_type(lp_wt)})_l')} offset += {lp_sz};")
                lines.append(f"{inner}for (int i = 0; i < _l; i++) {{ {bp_write('int16_t','span','offset',f'(short)((ReadOnlySpan<char>){stg})[i]')} offset += 2; }}")
                lines.append(f"{indent}}}")
            continue

        # ----- fixed struct array -----
        if arr is not None and is_struct_array(f, all_struct_names):
            lines.append(f"{indent}for (int i = 0; i < {arr}; i++)")
            lines.append(f"{indent}{{")
            lines.append(f"{inner}var _it = {stg}[i];")
            lines.append(f"{inner}if (!_it.TryWriteCore(span.Slice(offset), out int _n)) {{ bytesWritten = 0; return false; }}")
            lines.append(f"{inner}offset += _n;")
            lines.append(f"{indent}}}")
            continue

        # ----- fixed scalar / enum / bool array -----
        if arr is not None:
            lines.append(f"{indent}for (int i = 0; i < {arr}; i++)")
            lines.append(f"{indent}{{")
            lines.append(f"{inner}if (span.Length - offset < {sz}) {{ bytesWritten = 0; return false; }}")
            val = f"({cs_wire_type(wt)}){stg}[i]" if f['is_enum'] else f"{stg}[i]"
            lines.append(f"{inner}{bp_write(wt,'span','offset',val)} offset += {sz};")
            lines.append(f"{indent}}}")
            continue

        # ----- single nested struct -----
        if is_struct:
            lines.append(f"{indent}if (!{name}.TryWriteCore(span.Slice(offset), out int _n_{f['c_name']})) {{ bytesWritten = 0; return false; }}")
            lines.append(f"{indent}offset += _n_{f['c_name']};")
            continue

        # ----- single scalar / enum / bool / char / wchar -----
        lines.append(f"{indent}if (span.Length - offset < {sz}) {{ bytesWritten = 0; return false; }}")
        if f['is_enum']:
            val = f"({cs_wire_type(wt)}){name}"
        elif f['type'] == 'bool':
            val = f"_{f['c_name']}"
        elif f['type'] == 'char':
            val = f"_{f['c_name']}"
        elif f['type'] == 'wchar_t':
            val = f"_{f['c_name']}"
        else:
            val = name
        lines.append(f"{indent}{bp_write(wt,'span','offset',val)} offset += {sz};")

    lines.append(f"{indent}bytesWritten = offset;")
    lines.append(f"{indent}return true;")
    return lines


# ---------------------------------------------------------------------------
# SizeOfStruct body (variable-length modes only)
# ---------------------------------------------------------------------------

def gen_size_of_struct_body(fields, structs, prefix="            "):
    all_struct_names = set(structs.keys())
    lines = [f"{prefix}int size = 0;"]
    for f in fields:
        if f.get('is_length_for') is not None:
            continue
        name = f['cs_name']
        wt = f['wire_type']
        sz = WIRE_TYPE_SIZES.get(wt, 0)
        arr = f['array_len']
        is_struct = wt in all_struct_names
        stg = f"_{f['c_name']}"

        if arr is not None and f.get('length_field') is not None:
            if is_struct_array(f, all_struct_names):
                lines.append(f"{prefix}{{ int _c = {stg}Count; size += 4; for (int i = 0; i < _c; i++) size += {stg}[i].SizeOfStruct; }}")
            else:
                lines.append(f"{prefix}size += 4 + {stg}Count * {sz};")
            continue
        if arr is not None and is_char_array(f):
            lp_sz = length_prefix_size(arr)
            lines.append(f"{prefix}size += {lp_sz} + Buffers.TrimNullUtf8((ReadOnlySpan<byte>){stg}).Length;")
            continue
        if arr is not None and is_wchar_array(f):
            lp_sz = length_prefix_size(arr)
            lines.append(f"{prefix}size += {lp_sz} + Buffers.TrimNullUtf16((ReadOnlySpan<char>){stg}).Length * 2;")
            continue
        if arr is not None and is_struct_array(f, all_struct_names):
            lines.append(f"{prefix}for (int i = 0; i < {arr}; i++) size += {stg}[i].SizeOfStruct;")
            continue
        if arr is not None:
            lines.append(f"{prefix}size += {arr} * {sz};")
            continue
        if is_struct:
            lines.append(f"{prefix}size += {name}.SizeOfStruct;")
            continue
        lines.append(f"{prefix}size += {sz};")
    lines.append(f"{prefix}return size;")
    return lines


# ---------------------------------------------------------------------------
# Per-struct generation
# ---------------------------------------------------------------------------

def gen_struct_cs(struct_name, info, structs, enums, type_vis, member_vis,
                  reg, order, fixed_mode=True, big_endian=False, friendly=False,
                  prefix="", bltable=False, reorder=False):
    cs = info['cs_name']
    fields = info['fields']
    max_size = struct_wire_size(struct_name, structs, fixed_mode=fixed_mode)
    L = []
    # Layout attribute is opt-in (bltable and reorder are mutually exclusive,
    # enforced in main). Default: no attribute -> the C# default sequential
    # layout, which is faster than forcing Pack = 1 (packed fields can straddle
    # alignment boundaries and slow field access).
    if bltable:
        L.append("[StructLayout(LayoutKind.Sequential, Pack = 1)]")
    elif reorder:
        L.append("[StructLayout(LayoutKind.Auto)]")
    L.append(f"{type_vis}partial struct {cs}")
    L.append("{")
    L.append(f"    {member_vis}const int StructMaxSize = {max_size};")
    L.append("")

    if fields:
        L.extend(gen_field_members(fields, structs, member_vis, reg, order, friendly, fixed_mode, prefix))
        L.append("")
        if not fixed_mode:
            L.append(f"    {member_vis}int SizeOfStruct")
            L.append("    {")
            L.append("        get")
            L.append("        {")
            L.extend(gen_size_of_struct_body(fields, structs))
            L.append("        }")
            L.append("    }")
            L.append("")
    elif not fixed_mode:
        L.append(f"    {member_vis}int SizeOfStruct => 0;")
        L.append("")

    # cores
    L.append(f"    internal static bool TryReadCore(ReadOnlySpan<byte> span, out {cs} result, out int bytesRead)")
    L.append("    {")
    if fields:
        L.extend(gen_span_read_core(cs, fields, structs, big_endian, fixed_mode=fixed_mode))
    else:
        L.append("        result = default;")
        L.append("        bytesRead = 0;")
        L.append("        return true;")
    L.append("    }")
    L.append("")
    L.append(f"    internal bool TryWriteCore(Span<byte> span, out int bytesWritten)")
    L.append("    {")
    if fields:
        L.extend(gen_span_write_core(cs, fields, structs, big_endian, fixed_mode=fixed_mode))
    else:
        L.append("        bytesWritten = 0;")
        L.append("        return true;")
    L.append("    }")
    L.append("")

    # public span overloads
    L.append(f"    {member_vis}static bool TryRead(ReadOnlySpan<byte> span, out {cs} result, out int bytesRead)")
    L.append(f"        => TryReadCore(span, out result, out bytesRead);")
    L.append("")
    L.append(f"    {member_vis}bool TryWrite(Span<byte> destination, out int bytesWritten)")
    L.append(f"        => TryWriteCore(destination, out bytesWritten);")
    L.append("")

    # stream overloads
    L.append(f"    {member_vis}static bool TryRead(Stream stream, out {cs} result, out int bytesRead)")
    L.append("    {")
    L.append(f"        Span<byte> buf = stackalloc byte[StructMaxSize];")
    L.append(f"        int n = stream.Read(buf);")
    L.append(f"        if (n < StructMaxSize) {{ result = default; bytesRead = n; return false; }}")
    L.append(f"        return TryReadCore(buf, out result, out bytesRead);")
    L.append("    }")
    L.append("")
    L.append(f"    {member_vis}bool TryWrite(Stream stream, out int bytesWritten)")
    L.append("    {")
    L.append(f"        Span<byte> buf = stackalloc byte[StructMaxSize];")
    L.append(f"        if (!TryWriteCore(buf, out bytesWritten)) return false;")
    L.append(f"        stream.Write(buf.Slice(0, bytesWritten));")
    L.append(f"        return true;")
    L.append("    }")
    L.append("}")
    return "\n".join(L)


def gen_enum_cs(c_name, info, type_vis):
    cs = to_dotnet_name(c_name)
    backing = cs_wire_type(info['wire_type'])
    L = [f"{type_vis}enum {cs} : {backing}", "{"]
    for m, v in info['members']:
        L.append(f"    {m} = {v},")
    L.append("}")
    return "\n".join(L)


def gen_maxsize_cs(header_stem, max_size, type_vis, member_vis):
    cs = f"{to_dotnet_name(header_stem)}MaxSize"
    return f"{type_vis}struct {cs}\n{{\n    {member_vis}const int Value = {max_size};\n}}"


# ---------------------------------------------------------------------------
# File assembly
# ---------------------------------------------------------------------------

def generate_cs_file(header_path, structs, enums, namespace, is_public,
                     fixed_mode=True, big_endian=False, friendly=False,
                     bltable=False, reorder=False):
    type_vis = "public " if is_public else ""
    member_vis = "public " if is_public else "internal "
    stem = os.path.splitext(os.path.basename(header_path))[0]
    # Prefix inline-array helper names with the PascalCase header stem so they
    # don't collide with helpers generated from other headers into the same
    # namespace (e.g. interface.h -> InterfaceInlineUInt32Length1024).
    prefix = to_dotnet_name(stem)
    max_size = compute_max_wire_size(structs, fixed_mode=fixed_mode)

    reg, order = {}, []
    # generate struct bodies first so the inline registry is populated
    enum_lines = []
    for c_name, info in enums.items():
        enum_lines.append(gen_enum_cs(c_name, info, type_vis))
        enum_lines.append("")
    struct_lines = []
    for struct_name, info in structs.items():
        struct_lines.append(gen_struct_cs(struct_name, info, structs, enums,
                                           type_vis, member_vis, reg, order,
                                           fixed_mode=fixed_mode, big_endian=big_endian,
                                           friendly=friendly, prefix=prefix,
                                           bltable=bltable, reorder=reorder))
        struct_lines.append("")

    L = []
    L.append("// <auto-generated />")
    L.append("// Auto-generated by buffers_gen_cs_inline.py - do not edit manually.")
    L.append("#nullable disable")
    L.append("using System;")
    L.append("using System.Buffers.Binary;")
    L.append("using System.Diagnostics.CodeAnalysis;")
    L.append("using System.IO;")
    L.append("using System.Runtime.CompilerServices;")
    L.append("using System.Runtime.InteropServices;")
    L.append("using System.Text;")
    L.append("using Htcw;")
    L.append("")
    if namespace:
        L.append(f"namespace {namespace};")
        L.append("")
    L.extend(enum_lines)
    L.extend(gen_inline_helpers(order))
    L.append(gen_maxsize_cs(stem, max_size, type_vis, member_vis))
    L.append("")
    L.extend(struct_lines)
    L.append("#nullable restore")
    return "\n".join(L)


def generate_buffers_cs():
    return r'''// Auto-generated by buffers_gen_cs_inline.py - do not edit manually.
// Shared support types for wire buffer reading/writing.
#nullable disable
using System;
using System.Text;

namespace Htcw;

internal static class BuffersStatus
{
    public const int Success  =  0;
    public const int Eof      = -1;
    public const int ErrorEof = -2;
}

internal static class Buffers
{
    /// <summary>Bytes up to (not including) the first null byte.</summary>
    internal static ReadOnlySpan<byte> TrimNullUtf8(ReadOnlySpan<byte> span)
    {
        int i = span.IndexOf((byte)0);
        return i < 0 ? span : span.Slice(0, i);
    }

    /// <summary>Chars up to (not including) the first null char.</summary>
    internal static ReadOnlySpan<char> TrimNullUtf16(ReadOnlySpan<char> span)
    {
        int i = span.IndexOf('\0');
        return i < 0 ? span : span.Slice(0, i);
    }

    internal static string DecodeUtf8(ReadOnlySpan<byte> span)
        => Encoding.UTF8.GetString(TrimNullUtf8(span));

    /// <summary>Encode chars as UTF-8 into a fixed span, zero-padded, truncating
    /// only on a whole code-point boundary.</summary>
    internal static void EncodeUtf8(ReadOnlySpan<char> value, Span<byte> dest)
    {
        dest.Clear();
        if (value.IsEmpty) return;
        var encoder = Encoding.UTF8.GetEncoder();
        encoder.Convert(value, dest, flush: true,
            out int charsUsed, out int bytesUsed, out bool completed);
    }

    internal static bool[] ToBoolArray(ReadOnlySpan<byte> span)
    {
        var r = new bool[span.Length];
        for (int i = 0; i < span.Length; i++) r[i] = span[i] != 0;
        return r;
    }

    // fixed-length variant: copy into dest span, zero-fill remainder
    internal static void FromBoolArray(bool[] value, Span<byte> dest, int cap)
    {
        int n = value == null ? 0 : Math.Min(value.Length, cap);
        if (value != null && value.Length > cap)
            throw new ArgumentException("value exceeds capacity", nameof(value));
        for (int i = 0; i < n; i++) dest[i] = value[i] ? (byte)1 : (byte)0;
        for (int i = n; i < cap; i++) dest[i] = 0;
    }

    // length-prefixed variant: copy into dest span, report count
    internal static void FromBoolArray(bool[] value, Span<byte> dest, out int count)
    {
        int n = value == null ? 0 : value.Length;
        if (n > dest.Length)
            throw new ArgumentException("value exceeds capacity", nameof(value));
        for (int i = 0; i < n; i++) dest[i] = value[i] ? (byte)1 : (byte)0;
        count = n;
    }
}
#nullable restore
'''


def main():
    args = sys.argv[1:]
    namespace = None
    is_public = False
    gen_buffers = False
    out_dir = ""
    fixed_mode = False
    big_endian = False
    lengths_mode = False
    friendly = False
    bltable = False
    reorder = False
    while args and args[0].startswith('--'):
        opt = args.pop(0)
        if opt == '--namespace':
            if not args: error("--namespace requires an argument")
            namespace = args.pop(0)
        elif opt == '--public':   is_public = True
        elif opt == '--buffers':  gen_buffers = True
        elif opt == '--out':      out_dir = args.pop(0)
        elif opt == '--fixed':    fixed_mode = True
        elif opt == '--big-endian': big_endian = True
        elif opt == '--lengths':  lengths_mode = True
        elif opt == '--friendly': friendly = True
        elif opt == '--bltable':  bltable = True
        elif opt == '--reorder':  reorder = True
        else: error(f"Unknown option: {opt}")

    if len(args) != 1:
        print(f"Usage: {sys.argv[0]} [--fixed] [--big-endian] [--lengths] [--friendly] "
              f"[--bltable | --reorder] [--namespace <Ns>] [--public] [--buffers] <header.h>",
              file=sys.stderr)
        sys.exit(1)
    if lengths_mode and fixed_mode:
        error("--lengths and --fixed are mutually exclusive")
    if bltable and reorder:
        error("--bltable and --reorder are mutually exclusive")

    path = args[0]
    try:
        with open(path, 'r') as f:
            text = f.read()
    except OSError as e:
        error(f"Cannot open file: {e}")

    structs, enums = parse_header(text, lengths_mode=lengths_mode)
    if not structs:
        error("No structs found in header")

    stem = os.path.splitext(os.path.basename(path))[0]
    cs_stem = to_dotnet_name(stem)
    if len(out_dir) == 0:
        out_dir = os.path.dirname(path) or '.'
    cs_path = os.path.join(out_dir, f"{cs_stem}Buffers.cs")
    with open(cs_path, 'w') as f:
        f.write(generate_cs_file(path, structs, enums, namespace, is_public,
                                 fixed_mode=fixed_mode, big_endian=big_endian,
                                 friendly=friendly, bltable=bltable, reorder=reorder))
    print(f"Written: {cs_path}")
    if gen_buffers:
        buffers_path = os.path.join(out_dir, "Buffers.cs")
        with open(buffers_path, 'w') as f:
            f.write(generate_buffers_cs())
        print(f"Written: {buffers_path}")


if __name__ == '__main__':
    main()