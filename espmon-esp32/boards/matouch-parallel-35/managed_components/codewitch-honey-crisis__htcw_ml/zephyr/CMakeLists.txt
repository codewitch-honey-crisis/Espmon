# Copyright (c) 2023 honey the codewitch
#
# SPDX-License-Identifier: Apache-2.0

if(CONFIG_HTCW_ML)
zephyr_library()
#include_directories(${ZEPHYR_BASE}/lib/???/htcw_bits)
zephyr_include_directories(../src/)
zephyr_library_include_directories(../src/)
zephyr_interface_library_named(HTCW_ML)
zephyr_library_sources(../src/source/ml_reader_fa.cpp)
#zephyr_library_sources_ifdef(CONFIG_PARAM_DEF, file.cpp)
zephyr_library_link_libraries(HTCW_ML)
target_link_libraries(HTCW_ML INTERFACE zephyr_interface)

endif()