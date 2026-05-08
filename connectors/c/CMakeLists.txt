cmake_minimum_required(VERSION 3.21)

project(DotVectorC
    VERSION 0.1.0
    DESCRIPTION "DotVector native C connector"
    LANGUAGES C)

set(DOTVECTOR_C_DOTNET_CONFIGURATION "Release" CACHE STRING "Configuration used by dotnet publish.")
set_property(CACHE DOTVECTOR_C_DOTNET_CONFIGURATION PROPERTY STRINGS Debug Release)

set(DOTVECTOR_C_RID "" CACHE STRING "Target .NET runtime identifier: win-x64, win-x86, win-arm64/win-xarm, or linux-x64.")
set_property(CACHE DOTVECTOR_C_RID PROPERTY STRINGS win-x64 win-x86 win-arm64 win-xarm linux-x64)

option(DOTVECTOR_C_BUILD_EXAMPLES "Build C connector examples." ON)

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)

function(dotvector_detect_rid output_variable)
    if(DOTVECTOR_C_RID)
        set(_rid "${DOTVECTOR_C_RID}")
        if(_rid STREQUAL "win-xarm")
            set(_rid "win-arm64")
        endif()
    elseif(CMAKE_SYSTEM_NAME STREQUAL "Windows")
        string(TOLOWER "${CMAKE_VS_PLATFORM_NAME}" _platform)
        if(NOT _platform)
            string(TOLOWER "${CMAKE_SYSTEM_PROCESSOR}" _platform)
        endif()

        if(_platform MATCHES "^(arm64|aarch64)$")
            set(_rid "win-arm64")
        elseif(_platform MATCHES "^(win32|x86|i386|i686)$")
            set(_rid "win-x86")
        else()
            set(_rid "win-x64")
        endif()
    elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
        string(TOLOWER "${CMAKE_SYSTEM_PROCESSOR}" _processor)
        if(_processor MATCHES "^(x86_64|amd64)$")
            set(_rid "linux-x64")
        else()
            message(FATAL_ERROR "Only linux-x64 is currently supported by the C connector. Set DOTVECTOR_C_RID explicitly if adding a new RID.")
        endif()
    else()
        message(FATAL_ERROR "Unsupported platform '${CMAKE_SYSTEM_NAME}'. Supported RIDs: win-x64, win-x86, win-arm64, linux-x64.")
    endif()

    if(NOT _rid MATCHES "^(win-x64|win-x86|win-arm64|linux-x64)$")
        message(FATAL_ERROR "Unsupported DOTVECTOR_C_RID='${_rid}'. Supported RIDs: win-x64, win-x86, win-arm64/win-xarm, linux-x64.")
    endif()

    set(${output_variable} "${_rid}" PARENT_SCOPE)
endfunction()

dotvector_detect_rid(DOTVECTOR_C_EFFECTIVE_RID)
message(STATUS "DotVector C connector RID: ${DOTVECTOR_C_EFFECTIVE_RID}")

set(DOTVECTOR_C_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/include")
set(DOTVECTOR_REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../..")
set(DOTVECTOR_NATIVE_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/native/DotVector.Native/DotVector.Native.csproj")
set(DOTVECTOR_NATIVE_PROJECT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/native/DotVector.Native")
set(DOTVECTOR_NATIVE_PUBLISH_DIR "${CMAKE_CURRENT_BINARY_DIR}/native/${DOTVECTOR_C_EFFECTIVE_RID}/publish")
set(DOTVECTOR_NATIVE_STAMP "${CMAKE_CURRENT_BINARY_DIR}/native/${DOTVECTOR_C_EFFECTIVE_RID}/dotvector-native.stamp")

if(WIN32)
    set(DOTVECTOR_NATIVE_LIBRARY "${DOTVECTOR_NATIVE_PUBLISH_DIR}/DotVector.Native.dll")
    set(DOTVECTOR_NATIVE_IMPORT_LIBRARY "${DOTVECTOR_NATIVE_PROJECT_DIR}/bin/${DOTVECTOR_C_DOTNET_CONFIGURATION}/net10.0/${DOTVECTOR_C_EFFECTIVE_RID}/native/DotVector.Native.lib")
elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    set(DOTVECTOR_NATIVE_LIBRARY "${DOTVECTOR_NATIVE_PUBLISH_DIR}/DotVector.Native.so")
else()
    message(FATAL_ERROR "Unsupported CMake system '${CMAKE_SYSTEM_NAME}'.")
endif()

file(GLOB_RECURSE DOTVECTOR_NATIVE_SOURCE_FILES CONFIGURE_DEPENDS
    "${DOTVECTOR_NATIVE_PROJECT_DIR}/*.cs"
    "${DOTVECTOR_REPO_ROOT}/src/DotVector.Core/*.cs")

set(DOTVECTOR_DOTNET_BUILD_FILES
    "${DOTVECTOR_REPO_ROOT}/Directory.Build.props"
    "${DOTVECTOR_REPO_ROOT}/Directory.Packages.props"
    "${DOTVECTOR_NATIVE_PROJECT}"
    "${DOTVECTOR_REPO_ROOT}/src/DotVector.Core/DotVector.Core.csproj")

add_custom_command(
    OUTPUT "${DOTVECTOR_NATIVE_STAMP}"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "${DOTVECTOR_NATIVE_PUBLISH_DIR}"
    COMMAND "${DOTNET_EXECUTABLE}" publish "${DOTVECTOR_NATIVE_PROJECT}"
        -c "${DOTVECTOR_C_DOTNET_CONFIGURATION}"
        -r "${DOTVECTOR_C_EFFECTIVE_RID}"
        -o "${DOTVECTOR_NATIVE_PUBLISH_DIR}"
        /p:PublishAot=true
        /p:NativeLib=Shared
    COMMAND "${CMAKE_COMMAND}" -E touch "${DOTVECTOR_NATIVE_STAMP}"
    DEPENDS ${DOTVECTOR_DOTNET_BUILD_FILES} ${DOTVECTOR_NATIVE_SOURCE_FILES}
    COMMENT "Publishing DotVector.Native for ${DOTVECTOR_C_EFFECTIVE_RID}"
    VERBATIM)

add_custom_target(dotvector_native_publish ALL
    DEPENDS "${DOTVECTOR_NATIVE_STAMP}")

if(WIN32)
    add_library(dotvector_c SHARED IMPORTED GLOBAL)
    add_dependencies(dotvector_c dotvector_native_publish)
    set_target_properties(dotvector_c PROPERTIES
        IMPORTED_LOCATION "${DOTVECTOR_NATIVE_LIBRARY}"
        INTERFACE_INCLUDE_DIRECTORIES "${DOTVECTOR_C_INCLUDE_DIR}"
        IMPORTED_IMPLIB "${DOTVECTOR_NATIVE_IMPORT_LIBRARY}")
elseif(UNIX AND NOT APPLE)
    add_library(dotvector_c INTERFACE)
    add_dependencies(dotvector_c dotvector_native_publish)
    target_include_directories(dotvector_c INTERFACE "${DOTVECTOR_C_INCLUDE_DIR}")
    target_link_directories(dotvector_c INTERFACE "${DOTVECTOR_NATIVE_PUBLISH_DIR}")
    target_link_libraries(dotvector_c INTERFACE "-l:DotVector.Native.so")
endif()

if(DOTVECTOR_C_BUILD_EXAMPLES)
    add_executable(dotvector_quickstart examples/quick_start.c)
    target_compile_features(dotvector_quickstart PRIVATE c_std_99)
    target_link_libraries(dotvector_quickstart PRIVATE dotvector_c)
    if(UNIX AND NOT APPLE)
        add_dependencies(dotvector_quickstart dotvector_native_publish)
    endif()

    add_custom_command(TARGET dotvector_quickstart POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
            "${DOTVECTOR_NATIVE_LIBRARY}"
            "$<TARGET_FILE_DIR:dotvector_quickstart>"
        COMMENT "Copying DotVector native library next to dotvector_quickstart"
        VERBATIM)

    if(UNIX AND NOT APPLE)
        set_target_properties(dotvector_quickstart PROPERTIES
            BUILD_RPATH "$ORIGIN")
    endif()

    add_executable(dotvector_quickstart_full examples/quick_start_full.c)
    target_compile_features(dotvector_quickstart_full PRIVATE c_std_99)
    target_link_libraries(dotvector_quickstart_full PRIVATE dotvector_c)
    if(UNIX AND NOT APPLE)
        add_dependencies(dotvector_quickstart_full dotvector_native_publish)
    endif()

    add_custom_command(TARGET dotvector_quickstart_full POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
            "${DOTVECTOR_NATIVE_LIBRARY}"
            "$<TARGET_FILE_DIR:dotvector_quickstart_full>"
        COMMENT "Copying DotVector native library next to dotvector_quickstart_full"
        VERBATIM)

    if(UNIX AND NOT APPLE)
        set_target_properties(dotvector_quickstart_full PROPERTIES
            BUILD_RPATH "$ORIGIN")
    endif()
endif()

install(FILES "${DOTVECTOR_C_INCLUDE_DIR}/dotvector.h" DESTINATION include)
install(FILES "${DOTVECTOR_NATIVE_LIBRARY}" DESTINATION lib OPTIONAL)
