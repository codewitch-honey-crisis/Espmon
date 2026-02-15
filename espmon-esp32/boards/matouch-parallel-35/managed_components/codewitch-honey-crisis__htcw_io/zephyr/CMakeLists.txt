# Copyright (c) 2023 honey the codewitch
#
# SPDX-License-Identifier: Apache-2.0

if(CONFIG_HTCW_IO)
zephyr_library()
#include_directories(${ZEPHYR_BASE}/lib/???/htcw_bits)
#zephyr_compile_definitions(IO_NO_FS)
zephyr_include_directories(../src)
zephyr_library_include_directories(../src)
zephyr_interface_library_named(HTCW_IO)
zephyr_library_sources(../src/source/io_stream.cpp)
#zephyr_library_sources_ifdef(CONFIG_PARAM_DEF, file.cpp)
zephyr_library_link_libraries(HTCW_IO)
target_link_libraries(HTCW_IO INTERFACE zephyr_interface)

endif()