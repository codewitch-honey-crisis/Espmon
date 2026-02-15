cmake_minimum_required(VERSION 3.24)
if(NOT DEFINED IDF_TARGET)
    project(htcw_data VERSION 1.4.5)
    set(CMAKE_CXX_STANDARD 17)
    set(CMAKE_CXX_STANDARD_REQUIRED True)
    set(CMAKE_STATIC_LIBRARY_PREFIX "")
    set(CMAKE_SHARED_LIBRARY_PREFIX "")

    add_library(htcw_data INTERFACE)
    target_include_directories(htcw_data INTERFACE
        "${PROJECT_SOURCE_DIR}"
        "${PROJECT_BINARY_DIR}"
    )
else()
    idf_component_register(
        INCLUDE_DIRS "."
)
endif()