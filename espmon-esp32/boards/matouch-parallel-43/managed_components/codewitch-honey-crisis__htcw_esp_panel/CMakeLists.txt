if(EXISTS "${CMAKE_SOURCE_DIR}/main/custom_panel.h")
    set(MAIN_INCLUDE "${CMAKE_SOURCE_DIR}/main")
else()
    set(MAIN_INCLUDE "")
endif()

idf_component_register(
    SRCS "./src/source/panel.c"
    INCLUDE_DIRS "." "./src" ${MAIN_INCLUDE}
    REQUIRES driver freertos fatfs esp_lcd
)

# Hack: Add include directories from any LCD/touch driver components
foreach(comp IN LISTS BUILD_COMPONENTS)
    if(comp MATCHES "lcd" OR comp MATCHES "touch" OR comp MATCHES "power" OR comp MATCHES "expander" )
        idf_component_get_property(comp_dir ${comp} COMPONENT_DIR)
        target_link_libraries(${COMPONENT_LIB} INTERFACE idf::${comp})
        target_include_directories(${COMPONENT_LIB} PRIVATE "${comp_dir}/include")
    endif()
endforeach()