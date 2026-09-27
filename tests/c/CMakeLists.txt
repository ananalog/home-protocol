cmake_minimum_required(VERSION 3.16)
project(home_proto_tests C)
set(CMAKE_C_STANDARD 11)
add_compile_options(-Wall -Wextra -Werror)
add_executable(test_vectors
    test_vectors.c
    roundtrip.gen.c
    ../../c/src/home_tlv.c
    ../../c/src/home_proto.c)
target_include_directories(test_vectors PRIVATE ../../c/include)
enable_testing()
add_test(NAME vectors COMMAND test_vectors ${CMAKE_CURRENT_SOURCE_DIR}/../../testdata/vectors)
