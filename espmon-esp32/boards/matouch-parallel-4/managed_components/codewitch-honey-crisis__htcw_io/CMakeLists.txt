cmake_minimum_required(VERSION 3.24)
if(NOT DEFINED IDF_TARGET)
    project(htcw_io VERSION 1.4.0)
    set(CMAKE_CXX_STANDARD 17)
    set(CMAKE_CXX_STANDARD_REQUIRED True)
    set(CMAKE_STATIC_LIBRARY_PREFIX "")
    set(CMAKE_SHARED_LIBRARY_PREFIX "")

    # get dependecies
    include(FetchContent)
    FetchContent_Declare(htcw_bits
        GIT_REPOSITORY https://github.com/codewitch-honey-crisis/htcw_bits.git
        GIT_TAG 1.0.16
        GIT_SHALLOW ON
        FIND_PACKAGE_ARGS 1.0.16
    )

    FetchContent_MakeAvailable(htcw_bits)

    add_library(htcw_io src/source/io_stream.cpp)
    target_link_libraries(htcw_io htcw_bits)
    target_include_directories(htcw_io PUBLIC
        "${PROJECT_SOURCE_DIR}"
        "${PROJECT_SOURCE_DIR}/src"
        "${PROJECT_BINARY_DIR}"
    )
else()
    idf_component_register(
        SRCS "./src/source/io_stream.cpp"
        INCLUDE_DIRS "." "./src"
        REQUIRES htcw_bits
    )
endif()