# Copyright (c) 2023 honey the codewitch
#
# SPDX-License-Identifier: Apache-2.0

if(CONFIG_HTCW_BITS)

zephyr_library()

#include_directories(${ZEPHYR_BASE}/lib/???/htcw_bits)
zephyr_include_directories(..)
zephyr_library_include_directories(..)
zephyr_interface_library_named(HTCW_BITS)


#zephyr_library_sources_ifdef(CONFIG_PARAM_DEF, file.cpp)
zephyr_library_link_libraries(HTCW_BITS)
#target_link_libraries(HTCW_BITS INTERFACE zephyr_interface)

endif()