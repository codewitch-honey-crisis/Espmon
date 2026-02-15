cmake_minimum_required(VERSION 3.24)

project(libespmon VERSION 1.0)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED True)
set(CMAKE_STATIC_LIBRARY_PREFIX "")
set(CMAKE_SHARED_LIBRARY_PREFIX "")

add_compile_definitions(_UNICODE UNICODE)

include(FetchContent)

FetchContent_Declare(htcw_uix
    GIT_REPOSITORY https://github.com/codewitch-honey-crisis/uix.git
    GIT_TAG 1.8.14
    GIT_SHALLOW ON
    FIND_PACKAGE_ARGS 1.8.14
)
FetchContent_MakeAvailable(htcw_uix)

add_library(libespmon SHARED
    src/abi.cpp
)

target_link_libraries(libespmon htcw_uix)
target_include_directories(libespmon PUBLIC
    "${PROJECT_SOURCE_DIR}"
    "${PROJECT_BINARY_DIR}"
    "${PROJECT_SOURCE_DIR}/../common"
    "${PROJECT_SOURCE_DIR}/include"
    "${PROJECT_SOURCE_DIR}/src"
)

add_custom_command(TARGET libespmon POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy $<TARGET_FILE:libespmon> "..\\..\\EspMon\\libespmon.dll"
)