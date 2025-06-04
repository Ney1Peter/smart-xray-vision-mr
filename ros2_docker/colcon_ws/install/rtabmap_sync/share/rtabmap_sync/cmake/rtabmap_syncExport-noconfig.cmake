#----------------------------------------------------------------
# Generated CMake target import file.
#----------------------------------------------------------------

# Commands may need to know the format version.
set(CMAKE_IMPORT_FILE_VERSION 1)

# Import target "rtabmap_sync::rtabmap_sync" for configuration ""
set_property(TARGET rtabmap_sync::rtabmap_sync APPEND PROPERTY IMPORTED_CONFIGURATIONS NOCONFIG)
set_target_properties(rtabmap_sync::rtabmap_sync PROPERTIES
  IMPORTED_LOCATION_NOCONFIG "${_IMPORT_PREFIX}/lib/librtabmap_sync.so"
  IMPORTED_SONAME_NOCONFIG "librtabmap_sync.so"
  )

list(APPEND _cmake_import_check_targets rtabmap_sync::rtabmap_sync )
list(APPEND _cmake_import_check_files_for_rtabmap_sync::rtabmap_sync "${_IMPORT_PREFIX}/lib/librtabmap_sync.so" )

# Import target "rtabmap_sync::rtabmap_sync_plugins" for configuration ""
set_property(TARGET rtabmap_sync::rtabmap_sync_plugins APPEND PROPERTY IMPORTED_CONFIGURATIONS NOCONFIG)
set_target_properties(rtabmap_sync::rtabmap_sync_plugins PROPERTIES
  IMPORTED_LOCATION_NOCONFIG "${_IMPORT_PREFIX}/lib/librtabmap_sync_plugins.so"
  IMPORTED_SONAME_NOCONFIG "librtabmap_sync_plugins.so"
  )

list(APPEND _cmake_import_check_targets rtabmap_sync::rtabmap_sync_plugins )
list(APPEND _cmake_import_check_files_for_rtabmap_sync::rtabmap_sync_plugins "${_IMPORT_PREFIX}/lib/librtabmap_sync_plugins.so" )

# Commands beyond this point should not need to know the version.
set(CMAKE_IMPORT_FILE_VERSION)
