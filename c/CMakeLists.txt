# Home protocol C library.
# - ESP-IDF: home-firmware wraps these sources as its `home_proto` component
#   (components/home_proto/CMakeLists.txt points at external/home-protocol/c).
# - Plain CMake: add_subdirectory(c) gives the static library `home_proto`.
if(ESP_PLATFORM)
    idf_component_register(SRCS "src/home_tlv.c" "src/home_proto.c"
                           INCLUDE_DIRS "include")
else()
    cmake_minimum_required(VERSION 3.16)
    project(home_proto C)
    add_library(home_proto STATIC src/home_tlv.c src/home_proto.c)
    target_include_directories(home_proto PUBLIC include)
endif()
