cmake_minimum_required(VERSION 3.16)

# This is a meta-build script that builds all board configurations
# Usage: cmake -P build_all.cmake

# Read boards.json
file(READ "${CMAKE_CURRENT_LIST_DIR}/boards.json" BOARDS_JSON)

# Parse JSON manually (CMake doesn't have great JSON support, but we can work with it)
# This is a simplified parser - for production you might want a Python helper
string(JSON BOARDS_COUNT LENGTH "${BOARDS_JSON}" boards)
math(EXPR BOARDS_LAST "${BOARDS_COUNT} - 1")

message(STATUS "Building ${BOARDS_COUNT} board configurations...")

# Create firmware output directory
file(MAKE_DIRECTORY "${CMAKE_CURRENT_LIST_DIR}/firmware")

# Build each board
foreach(INDEX RANGE ${BOARDS_LAST})
    # Extract board info
    string(JSON BOARD_ID GET "${BOARDS_JSON}" boards ${INDEX} id)
    string(JSON BOARD_NAME GET "${BOARDS_JSON}" boards ${INDEX} name)
    string(JSON BOARD_SLUG GET "${BOARDS_JSON}" boards ${INDEX} slug)
    string(JSON BOARD_TARGET GET "${BOARDS_JSON}" boards ${INDEX} target)
    
    message(STATUS "")
    message(STATUS "========================================")
    message(STATUS "Building: ${BOARD_NAME}")
    message(STATUS "========================================")
    
    set(BOARD_DIR "${CMAKE_CURRENT_LIST_DIR}/boards/${BOARD_SLUG}")
    set(BUILD_DIR "${BOARD_DIR}/build")
    
    # Set target
    message(STATUS "Setting target to ${BOARD_TARGET}...")
    execute_process(
        COMMAND idf.py -C "${BOARD_DIR}" set-target ${BOARD_TARGET}
        RESULT_VARIABLE RESULT
        OUTPUT_VARIABLE OUTPUT
        ERROR_VARIABLE ERROR
    )
    
    if(NOT RESULT EQUAL 0)
        message(FATAL_ERROR "Failed to set target for ${BOARD_NAME}: ${ERROR}")
    endif()
    
    # Build
    message(STATUS "Building ${BOARD_NAME}...")
    execute_process(
        COMMAND idf.py -C "${BOARD_DIR}" build
        RESULT_VARIABLE RESULT
        OUTPUT_VARIABLE OUTPUT
        ERROR_VARIABLE ERROR
    )
    
    if(NOT RESULT EQUAL 0)
        message(FATAL_ERROR "Failed to build ${BOARD_NAME}: ${ERROR}")
    endif()
    
    # Create firmware output directory for this board
    set(FIRMWARE_OUT_DIR "${CMAKE_CURRENT_LIST_DIR}/firmware/${BOARD_SLUG}")
    file(MAKE_DIRECTORY "${FIRMWARE_OUT_DIR}")
    
    # Copy binaries
    message(STATUS "Copying binaries for ${BOARD_NAME}...")
    
    file(COPY "${BUILD_DIR}/bootloader/bootloader.bin"
         DESTINATION "${FIRMWARE_OUT_DIR}")
    
    file(COPY "${BUILD_DIR}/partition_table/partition-table.bin"
         DESTINATION "${FIRMWARE_OUT_DIR}")
    
    file(COPY "${BUILD_DIR}/espmon.bin"
         DESTINATION "${FIRMWARE_OUT_DIR}")
    
    file(RENAME "${FIRMWARE_OUT_DIR}/espmon.bin" 
                "${FIRMWARE_OUT_DIR}/firmware.bin")
    
    message(STATUS "✓ ${BOARD_NAME} built successfully")
endforeach()

# Copy boards.json to firmware directory as manifest
file(COPY "${CMAKE_CURRENT_LIST_DIR}/boards.json"
     DESTINATION "${CMAKE_CURRENT_LIST_DIR}/firmware")

message(STATUS "")
message(STATUS "========================================")
message(STATUS "All boards built successfully!")
message(STATUS "Firmware binaries available in: firmware/")
message(STATUS "========================================")
