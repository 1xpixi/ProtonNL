cmake_minimum_required(VERSION 3.20)
project(ProtonNL LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

add_library(ProtonNL.Internal SHARED
    Internal/dllmain.cpp
    Internal/ManagedHost.cpp
    Internal/ManagedHost.h
    Internal/Log.cpp
    Internal/Log.h
)

target_include_directories(ProtonNL.Internal PRIVATE
    Internal
    Internal/third_party
)

target_compile_definitions(ProtonNL.Internal PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN)
target_link_libraries(ProtonNL.Internal PRIVATE shlwapi)

add_executable(ProtonNL.Loader
    Loader/main.cpp
)

target_compile_definitions(ProtonNL.Loader PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN)
target_link_libraries(ProtonNL.Loader PRIVATE shlwapi)

if (MSVC)
    target_compile_options(ProtonNL.Internal PRIVATE /W4)
    target_compile_options(ProtonNL.Loader PRIVATE /W4)
endif()

find_program(DOTNET_EXE dotnet REQUIRED)

add_custom_command(TARGET ProtonNL.Internal POST_BUILD
    COMMAND ${DOTNET_EXE} publish ${CMAKE_SOURCE_DIR}/Hook/ProtonNL.Hook.csproj
            -c $<IF:$<CONFIG:Debug>,Debug,Release>
            -r win-x64
            --self-contained false
            -o $<TARGET_FILE_DIR:ProtonNL.Internal>
    COMMENT "Publishing ProtonNL.Hook next to Internal.dll"
)

add_dependencies(ProtonNL.Loader ProtonNL.Internal)
