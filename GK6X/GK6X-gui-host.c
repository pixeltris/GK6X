// On Mac / Linux this can be compiled with "gcc GK6X-gui-host.c"
// On Windows this can be compiled with "cl GK6X-gui-host.c /link /SUBSYSTEM:WINDOWS /ENTRY:"mainCRTStartup" /out:../Build/GK6X-gui-host.exe" (via Visual Studio dev tools cmd)
// - Download .NET Core https://github.com/dotnet/core#download-the-latest-net-core-sdk
// - Copy "/shared/Microsoft.NETCore.App/X.X.X/" to "/Build/CoreCLR"
// - Run the GK6X-gui-host binary

// GK6X-gui.bat builds GK6X in GUI mode (no console). GK6X-gui-host.c is used to host that GUI within a native application under .NET Core (portability)

// TODO: Add support for Linux

// This is basically a copy of the coreclr hosts but in C rather than C++
// https://github.com/dotnet/coreclr/blob/master/src/coreclr/hosts

#define ASSEMBLY_NAME "GK6X-gui"
#define ASSEMBLY_ENTRY_POINT_CLASS "GK6X.Program"
#define ASSEMBLY_ENTRY_POINT_METHOD "DllMain"

#if defined(WIN32) || defined(_WIN32) || defined(__WIN32)
#define PLATFORM_WINDOWS 1
#else
#define PLATFORM_WINDOWS 0
#endif

#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>
#include <sys/stat.h>
#if PLATFORM_WINDOWS
#include <windows.h>
#else
#include <dlfcn.h>
#endif

#ifndef HRESULT
#define HRESULT int
#endif
#ifndef SUCCEEDED
#define SUCCEEDED(hr) ((hr) >= 0)
#endif
#ifndef FAILED
#define FAILED(hr) ((hr) < 0)
#endif

#if PLATFORM_WINDOWS
#define PATH_MAX MAX_PATH
#define CORE_CLR_DLL "coreclr.dll"
#define CORE_CLR_DLL_CALLCONV WINAPI
#define CORE_CLR_FILE_SPLIT ";"
#define SLASH_CHAR '\\'
#define SLASH_CHAR_STR "\\"
#else
#define CORE_CLR_DLL "libcoreclr.dylib"
#define CORE_CLR_DLL_CALLCONV
#define CORE_CLR_FILE_SPLIT ":"
#define SLASH_CHAR '/'
#define SLASH_CHAR_STR "/"
#endif

typedef int(CORE_CLR_DLL_CALLCONV *import__coreclr_initialize)(const char* exePath, const char* appDomainFriendlyName, int propertyCount, const char** propertyKeys, const char** propertyValues, void** hostHandle, unsigned int* domainId);
typedef int(CORE_CLR_DLL_CALLCONV *import__coreclr_shutdown)(void* hostHandle, unsigned int domainId);
typedef int(CORE_CLR_DLL_CALLCONV *import__coreclr_shutdown_2)(void* hostHandle, unsigned int domainId, int* latchedExitCode);
typedef int(CORE_CLR_DLL_CALLCONV *import__coreclr_create_delegate)(void* hostHandle, unsigned int domainId, const char* entryPointAssemblyName, const char* entryPointTypeName, const char* entryPointMethodName, void** delegate);
typedef int(CORE_CLR_DLL_CALLCONV *import__coreclr_execute_assembly)(void* hostHandle, unsigned int domainId, int argc, const char** argv, const char* managedAssemblyPath, unsigned int* exitCode);

import__coreclr_initialize coreclr_initialize;
import__coreclr_shutdown coreclr_shutdown;
import__coreclr_shutdown_2 coreclr_shutdown_2;
import__coreclr_create_delegate coreclr_create_delegate;
import__coreclr_execute_assembly coreclr_execute_assembly;

// The signature of the C# entry point method
typedef int(*ManagedEntryPointSig)(const char* arg);

void* LoadDll(char* path)
{
#if PLATFORM_WINDOWS
    return LoadLibrary(path);
#else
    return dlopen(path, RTLD_NOW | RTLD_LOCAL);
#endif
}

void* GetDllExport(void* handle, const char* path)
{
#if PLATFORM_WINDOWS
    return GetProcAddress(handle, path);
#else
    return dlsym(handle, path);
#endif
}

int FileExists(char* path)
{
#if PLATFORM_WINDOWS
    WIN32_FIND_DATA findFileData;
    HANDLE handle = FindFirstFile(path, &findFileData);
    int found = handle != INVALID_HANDLE_VALUE;
    if (found)
    {
        FindClose(handle);
    }
    return found;
#else
    struct stat sb;
    return stat(path, &sb) != -1;
#endif
}

char* GetFullPath(const char* path, char* resolvedPath)
{
#if PLATFORM_WINDOWS
    return _fullpath(resolvedPath, path, PATH_MAX);
#else
    return realpath(path, resolvedPath);
#endif
}

int main(int argv, const char** argc)
{
    if (argv <= 0)
    {
        printf("Invalid args.\n");
        return 0;
    }
    char dirPath[PATH_MAX+1];
    char currentBinaryPath[PATH_MAX+1];
    char coreCrlDir[PATH_MAX+1];
    char coreCrlDllPath[PATH_MAX+1];
    char assemblyDir[PATH_MAX+1];
    char assemblyPath[PATH_MAX+1];
    if (GetFullPath(argc[0], currentBinaryPath) == NULL)
    {
        printf("Failed to find path of the binary '%s'.\n", argc[0]);
        return 0;
    }
    
    strcpy(dirPath, currentBinaryPath);
    size_t len = strlen(dirPath);
    char* dirPathEnd = strrchr(dirPath, SLASH_CHAR);
    if (dirPathEnd == NULL)
    {
        printf("Invalid binary path '%s'.\n", dirPath);
        return 0;
    }
    dirPathEnd[0] = '\0';
    
    // Need the assembly directory for APP_PATHS
    strcpy(assemblyDir, dirPath);
    
    strcpy(assemblyPath, assemblyDir);
    strcat(assemblyPath, SLASH_CHAR_STR);
    strcat(assemblyPath, ASSEMBLY_NAME);
    strcat(assemblyPath, ".exe");
    if (!FileExists(assemblyPath))
    {
        printf("Failed to find managed assembly '%s'.\n", assemblyPath);
        return 0;
    }
    
    // Need the .NET Core directory for APP_PATHS
    strcpy(coreCrlDir, dirPath);
    strcat(coreCrlDir, SLASH_CHAR_STR);
    strcat(coreCrlDir, "CoreCLR");
    
    strcpy(coreCrlDllPath, coreCrlDir);
    strcat(coreCrlDllPath, SLASH_CHAR_STR);
    strcat(coreCrlDllPath, CORE_CLR_DLL);
    if (!FileExists(coreCrlDllPath))
    {
        printf("Failed to find .NET Core '%s'.\n", coreCrlDllPath);
        return 0;
    }
    
    void* dllHandle = LoadDll(coreCrlDllPath);
    if (dllHandle == NULL)
    {
        printf("Failed to load .NET Core.\n");
        return 0;
    }
    
    coreclr_initialize = (import__coreclr_initialize)GetDllExport(dllHandle, "coreclr_initialize");
    coreclr_shutdown = (import__coreclr_shutdown)GetDllExport(dllHandle, "coreclr_shutdown");
    coreclr_shutdown_2 = (import__coreclr_shutdown_2)GetDllExport(dllHandle, "coreclr_shutdown_2");
    coreclr_create_delegate = (import__coreclr_create_delegate)GetDllExport(dllHandle, "coreclr_create_delegate");
    coreclr_execute_assembly = (import__coreclr_execute_assembly)GetDllExport(dllHandle, "coreclr_execute_assembly");
    if (coreclr_initialize == NULL ||
        coreclr_shutdown == NULL ||
        coreclr_shutdown_2 == NULL ||
        coreclr_create_delegate == NULL ||
        coreclr_execute_assembly == NULL)
    {
        printf("Failed to find .NET Core functions.\n");
        return 0;
    }
    
    // Use both the CoreCLR directory and the target assembly directory for APP_PATHS so that
    // it can resolve CoreCLR system assemblies
    char appPaths[0x10000] = {0};
    strcat(appPaths, coreCrlDir);
    strcat(appPaths, CORE_CLR_FILE_SPLIT);
    strcat(appPaths, assemblyDir);
    
    // We may need to trust more assemblies, for now just add our target assembly
    char trustedAssemblies[0x10000] = {0};
    strcat(trustedAssemblies, assemblyPath);
    
    const char* propertyKeys[] =
    {
        "TRUSTED_PLATFORM_ASSEMBLIES",
        "APP_PATHS"
    };
    const char* propertyValues[] =
    {
        // TRUSTED_PLATFORM_ASSEMBLIES
        trustedAssemblies,
        // APP_PATHS
        appPaths
    };
    
    void* coreCLRHandle;
    unsigned int domainId = 0;
    
    int hr = coreclr_initialize(
            currentBinaryPath,
            "GK6XAppDomain",
            sizeof(propertyValues) / sizeof(char*),
            propertyKeys,
            propertyValues,
            &coreCLRHandle,
            &domainId);
    
    if (FAILED(hr))
    {
        printf("oreclr_initialize failed. ErrorCode: 0x%08x (%u)\n\n"
               "TRUSTED_PLATFORM_ASSEMBLIES: %s\n\nAPP_PATHS: %s\n\nCurrent exe path: %s\n",
                hr, hr, trustedAssemblies, appPaths, currentBinaryPath);
        return 0;
    }

    ManagedEntryPointSig entryPoint;
    hr = coreclr_create_delegate(
            coreCLRHandle,
            domainId,
            ASSEMBLY_NAME,
            ASSEMBLY_ENTRY_POINT_CLASS,
            ASSEMBLY_ENTRY_POINT_METHOD,
            (void**)(&entryPoint));
    
    if (FAILED(hr))
    {
        printf("coreclr_create_delegate failed. ErrorCode: 0x%08x (%u)\n", hr, hr);
        return 0;
    }
    
    entryPoint(NULL);
    return 0;
}