// On Mac / Linux this can be compiled with "gcc gui.c"
// On Windows this can be compiled with "cl gui.c" (via Visual Studio dev tools cmd)
// NOTE: Currently only targets Mac

// This is basically a copy of the coreclr hosts but in C rather than C++
// https://github.com/dotnet/coreclr/blob/master/src/coreclr/hosts

#define ASSEMBLY_NAME "GK6X"
#define ASSEMBLY_ENTRY_POINT_CLASS "GK6X.Program"
#define ASSEMBLY_ENTRY_POINT_METHOD "DllMain"

#include <stdio.h>
#include <stdint.h>
#include <dlfcn.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <limits.h>

#ifndef HRESULT
#define HRESULT int
#endif
#ifndef SUCCEEDED
#define SUCCEEDED(hr) ((hr) >= 0)
#endif
#ifndef FAILED
#define FAILED(hr) ((hr) < 0)
#endif

#define CORE_CLR_DLL "libcoreclr.dylib"

#if PLATFORM_WINDOWS
#define CORE_CLR_FILE_SPLIT ";"
#else
#define CORE_CLR_FILE_SPLIT ":"
#endif

typedef int(*import__coreclr_initialize)(const char* exePath, const char* appDomainFriendlyName, int propertyCount, const char** propertyKeys, const char** propertyValues, void** hostHandle, unsigned int* domainId);
typedef int(*import__coreclr_shutdown)(void* hostHandle, unsigned int domainId);
typedef int(*import__coreclr_shutdown_2)(void* hostHandle, unsigned int domainId, int* latchedExitCode);
typedef int(*import__coreclr_create_delegate)(void* hostHandle, unsigned int domainId, const char* entryPointAssemblyName, const char* entryPointTypeName, const char* entryPointMethodName, void** delegate);
typedef int(*import__coreclr_execute_assembly)(void* hostHandle, unsigned int domainId, int argc, const char** argv, const char* managedAssemblyPath, unsigned int* exitCode);

import__coreclr_initialize coreclr_initialize;
import__coreclr_shutdown coreclr_shutdown;
import__coreclr_shutdown_2 coreclr_shutdown_2;
import__coreclr_create_delegate coreclr_create_delegate;
import__coreclr_execute_assembly coreclr_execute_assembly;

// The signature of the C# entry point method
typedef int(*ManagedEntryPointSig)(const char* arg);

void* LoadDll(char* path)
{
    return dlopen(path, RTLD_NOW | RTLD_LOCAL);
}

void* GetDllExport(void* handle, const char* path)
{
    return dlsym(handle, path);
}

int FileExists(char* path)
{
    struct stat sb;
    return stat(path, &sb) != -1;
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
    if (realpath(argc[0], currentBinaryPath) == NULL)
    {
        printf("Failed to find path of the binary '%s'.\n", argc[0]);
        return 0;
    }
    
    strcpy(dirPath, currentBinaryPath);
    size_t len = strlen(dirPath);
    char* dirPathEnd = strrchr(dirPath, '/');
    if (dirPathEnd == NULL)
    {
        printf("Invalid binary path '%s'.\n", dirPath);
        return 0;
    }
    dirPathEnd[0] = '\0';
    
    // Need the assembly directory for APP_PATHS
    strcpy(assemblyDir, dirPath);
    strcat(assemblyDir, "/Build");
    
    strcpy(assemblyPath, assemblyDir);
    strcat(assemblyPath, "/GK6X.exe");
    if (!FileExists(assemblyPath))
    {
        printf("Failed to find managed assembly '%s'.\n", assemblyPath);
        return 0;
    }
    
    // Need the .NET Core directory for APP_PATHS
    strcpy(coreCrlDir, dirPath);
    strcat(coreCrlDir, "/Build/CoreCLR");
    
    strcpy(coreCrlDllPath, coreCrlDir);
    strcat(coreCrlDllPath, "/");
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
    char appPaths[0x100000] = {0};
    strcat(appPaths, coreCrlDir);
    strcat(appPaths, CORE_CLR_FILE_SPLIT);
    strcat(appPaths, assemblyDir);
    
    // We may need to trust more assemblies, for now just add our target assembly
    char trustedAssemblies[0x100000] = {0};
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
    unsigned int domainId;
    
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
