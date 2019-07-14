// Copy of https://github.com/pixeltris/SonyAlphaUSB/blob/master/WIA%20Logger/SonyAlphaUSBLoader.cpp
// Taken 30th June 2019
#include <windows.h>
#include "min_minhook.h"
#include <stdio.h>
#include <metahost.h>
#include <unordered_set>

#pragma comment(lib, "mscoree.lib")
#pragma comment(lib, "user32.lib")

#ifdef __cplusplus
#define LIBRARY_API extern "C" __declspec (dllexport)
#else
#define LIBRARY_API __declspec (dllexport)
#endif

BOOL hooksInitialized = FALSE;

LIBRARY_API MH_STATUS WL_InitHooks()
{
	if (!hooksInitialized)
	{
		hooksInitialized = TRUE;
		return MH_Initialize();
	}
	return MH_OK;
}

LIBRARY_API MH_STATUS WL_HookFunction(LPVOID target, LPVOID detour, LPVOID* original)
{
	MH_STATUS status = MH_CreateHook(target, detour, original);
	if (status == MH_OK)
	{
		return MH_EnableHook(target);
	}
	return status;
}

LIBRARY_API MH_STATUS WL_CreateHook(LPVOID target, LPVOID detour, LPVOID* original)
{
	return MH_CreateHook(target, detour, original);
}

LIBRARY_API MH_STATUS WL_RemoveHook(LPVOID target)
{
	return MH_RemoveHook(target);
}

LIBRARY_API MH_STATUS WL_EnableHook(LPVOID target)
{
	return MH_EnableHook(target);
}

LIBRARY_API MH_STATUS WL_DisableHook(LPVOID target)
{
	return MH_DisableHook(target);
}

void* currentDevice = NULL;
std::unordered_set<void*> deviceHandles;
typedef BOOL (WINAPI *CloseHandleCallback)(void* handle);
CloseHandleCallback closeHandleOriginal = NULL;
void* kernel32_closeHandle;
BOOL WINAPI CloseHandleHook(void* handle)
{
	if (handle && deviceHandles.find(handle) != deviceHandles.end())
	{
		if (handle == currentDevice)
		{
			currentDevice = NULL;
		}
		deviceHandles.erase(handle);
	}
	return closeHandleOriginal(handle);
}
LIBRARY_API void WINAPI HookCloseHandle(void** currentDeviceAddr)
{
	*currentDeviceAddr = &currentDevice;
	
	HMODULE kernel32 = GetModuleHandle("Kernel32.dll");
	kernel32_closeHandle = GetProcAddress(kernel32, "CloseHandle");
	MH_CreateHook(kernel32_closeHandle, &CloseHandleHook, (void**)&closeHandleOriginal);
	WL_EnableHook(kernel32_closeHandle);
}
LIBRARY_API void WINAPI OnDeviceHandle(void* handle, BOOL add)
{
	if (add)
	{
		deviceHandles.insert(handle);
	}
	else
	{
		deviceHandles.erase(handle);
	}
}
LIBRARY_API BOOL WINAPI IsKnownDevice(void* handle)
{
	return deviceHandles.find(handle) != deviceHandles.end();
}

void LoadDotNet()
{
    ICLRMetaHost* metaHost;
    ICLRRuntimeHost* runtimeHost;
    ICLRRuntimeInfo *runtimeInfo;
    HRESULT result = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&metaHost);
    if (!SUCCEEDED(result))
    {
        return;
    }
    
    result = metaHost->GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo, (LPVOID*)&runtimeInfo);
    if (!SUCCEEDED(result))
    {
        return;
    }
    
    result = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&runtimeHost);
    if (!SUCCEEDED(result))
    {
        return;
    }
    
    result = runtimeHost->Start();
    if (!SUCCEEDED(result))
    {
        return;
    }
    
    result = runtimeInfo->BindAsLegacyV2Runtime();
    if (!SUCCEEDED(result))
    {
        return;
    }
    
    result = runtimeHost->ExecuteInDefaultAppDomain(L"GK6X.exe", L"GK6X.CommandLogger", L"DllMain", NULL, NULL);
}

BOOL WINAPI DllMain(HINSTANCE hDll, DWORD dwReason, LPVOID lpReserved)
{
    switch (dwReason)
    {
        case DLL_PROCESS_ATTACH:
            DisableThreadLibraryCalls(hDll);
            CreateThread(NULL, NULL, (LPTHREAD_START_ROUTINE)LoadDotNet, NULL, NULL, NULL);
            break;
    }
    return TRUE;
}