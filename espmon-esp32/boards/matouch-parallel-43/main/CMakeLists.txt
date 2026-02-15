set(SHARED_MAIN_DIR "${CMAKE_CURRENT_LIST_DIR}/../../../shared/main")
set(COMMON_DIR "${CMAKE_CURRENT_LIST_DIR}/../../../../common")

file(GLOB_RECURSE SHARED_SOURCES "${SHARED_MAIN_DIR}/*.cpp" "${SHARED_MAIN_DIR}/*.c")

idf_component_register(
    SRCS ${SHARED_SOURCES}
    INCLUDE_DIRS 
        "${SHARED_MAIN_DIR}"
        "${COMMON_DIR}"
	"${CMAKE_CURRENT_LIST_DIR}"
    REQUIRES 
        freertos
        esp_driver_uart
        htcw_esp_panel
        htcw_uix
)