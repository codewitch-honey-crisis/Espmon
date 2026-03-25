cmake_minimum_required(VERSION 3.24)
if(NOT DEFINED IDF_TARGET)
    project(htcw_frame VERSION 0.1.5)
    set(CMAKE_CXX_STANDARD 17)
    set(CMAKE_CXX_STANDARD_REQUIRED True)
    set(CMAKE_STATIC_LIBRARY_PREFIX "")
    set(CMAKE_SHARED_LIBRARY_PREFIX "")

    add_library(htcw_frame src/source/frame.c)
    target_include_directories(htcw_frame PUBLIC
        "${PROJECT_SOURCE_DIR}"
        "${PROJECT_SOURCE_DIR}/src"
        "${PROJECT_BINARY_DIR}"
    )
else()
    idf_component_register(
        SRCS "./src/source/frame.c"
        INCLUDE_DIRS "./src"
)
endif()