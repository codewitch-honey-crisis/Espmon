cmake_minimum_required(VERSION 3.24)
if(NOT DEFINED IDF_TARGET)
    project(htcw_ml VERSION 0.2.9)
    set(CMAKE_CXX_STANDARD 17)
    set(CMAKE_CXX_STANDARD_REQUIRED True)
    set(CMAKE_STATIC_LIBRARY_PREFIX "")
    set(CMAKE_SHARED_LIBRARY_PREFIX "")

    include(FetchContent)
    FetchContent_Declare(htcw_io
        GIT_REPOSITORY https://github.com/codewitch-honey-crisis/htcw_io.git
        GIT_TAG 1.4.1
        GIT_SHALLOW ON
        FIND_PACKAGE_ARGS 1.4.1
    )

    FetchContent_MakeAvailable(htcw_io)

    add_library(htcw_ml src/source/ml_reader_fa.cpp)
    target_link_libraries(htcw_ml htcw_io htcw_bits)
    target_include_directories(htcw_ml PUBLIC
        "${PROJECT_SOURCE_DIR}"
        "${PROJECT_SOURCE_DIR}/src"
        "${PROJECT_BINARY_DIR}"
    )

    # unit tests
    # find_package(Catch2 3 QUIET)

    # if(NOT Catch2_FOUND)
    #     include(FetchContent)
    #     FetchContent_Declare(
    #         Catch2
    #         GIT_REPOSITORY https://github.com/catchorg/Catch2.git
    #         GIT_TAG v3.4.0
    #         GIT_SHALLOW ON
    #     )
    #     FetchContent_MakeAvailable(Catch2)
    # endif()

    # add_executable(ml_tests ml_tests.cpp)
    # target_link_libraries(ml_tests htcw_ml htcw_io htcw_bits Catch2::Catch2WithMain)

    #include(CTest)
    #include(Catch)
    #catch_discover_tests(ml_tests)
else()
    idf_component_register(
        SRCS "./src/source/ml_reader_fa.cpp"
        INCLUDE_DIRS "." "./src"
        REQUIRES htcw_io
    )
endif()