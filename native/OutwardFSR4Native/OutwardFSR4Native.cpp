// OutwardFSR4Native - ponte DX11 -> DX12 per eseguire AMD FSR 4 (FSR SDK 2.x) in un gioco DirectX 11.
//
// Funzionamento per ogni frame (tutto sul thread di rendering di Unity, tramite IssuePluginEventAndData):
//   1. DX11: copia colore / profondita' / vettori di movimento nelle texture condivise e segnala una fence condivisa.
//   2. DX12: la coda attende la fence SULLA GPU, registra ed esegue il dispatch di FSR, poi segnala la fence.
//   3. DX11: attende la fence SULLA GPU e copia il risultato nella texture di output di Unity.
// Nessuna attesa lato CPU nel percorso normale: la sincronizzazione avviene interamente sulla GPU.

#define WIN32_LEAN_AND_MEAN
#ifndef _WINDOWS
#define _WINDOWS
#endif
#include <windows.h>
#include <d3d11_4.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <deque>
#include <mutex>
#include <string>
#include <vector>

#include "ffx_api.h"
#include "ffx_api_loader.h"
#include "dx12/ffx_api_dx12.h"
#include "ffx_upscale.h"

using Microsoft::WRL::ComPtr;

#define OFSR_EXPORT extern "C" __declspec(dllexport)

// ---------------------------------------------------------------------------------------------
// Parametri per frame, compilati dal lato C# (layout identico alla struct NativeFrameParams in C#)
// ---------------------------------------------------------------------------------------------
#pragma pack(push, 8)
struct FrameParams
{
    void* color;        // ID3D11Texture2D* (render size, RGBA16F)
    void* depth;        // ID3D11Texture2D* (render size, R32F)
    void* motion;       // ID3D11Texture2D* (render size, RG16F)
    void* output;       // ID3D11Texture2D* (display size, RGBA16F)
    int   renderW, renderH;
    int   displayW, displayH;
    float jitterX, jitterY;
    float mvScaleX, mvScaleY;
    float frameTimeMs;
    float sharpness;
    float cameraNear, cameraFar;
    float fovY;
    int   reset;
    int   enableSharpening;
    int   createFlags;  // combinazione di FFX_UPSCALE_ENABLE_*
};
#pragma pack(pop)

enum EventId
{
    EventFrame = 1,
    EventRelease = 2,
};

// ---------------------------------------------------------------------------------------------
// Log verso il lato C# (letto e scritto nel log di BepInEx)
// ---------------------------------------------------------------------------------------------
static std::mutex g_logMutex;
static std::deque<std::string> g_log;

static void Log(const char* fmt, ...)
{
    char buf[1024];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (g_log.size() < 256) g_log.emplace_back(buf);
}

static void FfxMessageCallback(uint32_t type, const wchar_t* message)
{
    char buf[1024];
    WideCharToMultiByte(CP_UTF8, 0, message, -1, buf, sizeof(buf), nullptr, nullptr);
    Log("[FFX %s] %s", type == FFX_API_MESSAGE_TYPE_ERROR ? "errore" : "avviso", buf);
}

// ---------------------------------------------------------------------------------------------
// Stato globale
// ---------------------------------------------------------------------------------------------
struct SharedTex
{
    ComPtr<ID3D12Resource> r12;
    ComPtr<ID3D11Texture2D> t11;
    DXGI_FORMAT format = DXGI_FORMAT_UNKNOWN;
    int w = 0, h = 0;
};

static const int kAllocators = 3;

static volatile LONG g_status = 0;          // 0 = non inizializzato, 1 = attivo, <0 = errore
static bool g_deviceReady = false;
static bool g_deviceFailed = false;

static ComPtr<ID3D11Device5> g_dev11;
static ComPtr<ID3D11DeviceContext4> g_ctx11;
static ComPtr<ID3D12Device> g_dev12;
static ComPtr<ID3D12CommandQueue> g_queue;
static ComPtr<ID3D12CommandAllocator> g_alloc[kAllocators];
static UINT64 g_allocFence[kAllocators] = {};
static ComPtr<ID3D12GraphicsCommandList> g_list;
static ComPtr<ID3D12Fence> g_fence12;
static ComPtr<ID3D11Fence> g_fence11;
static UINT64 g_fenceValue = 0;
static HANDLE g_fenceEvent = nullptr;
static int g_frameIndex = 0;

static HMODULE g_ffxModule = nullptr;
static ffxFunctions g_ffx = {};
static ffxContext g_ffxContext = nullptr;
static int g_ctxRenderW = 0, g_ctxRenderH = 0, g_ctxDisplayW = 0, g_ctxDisplayH = 0, g_ctxFlags = -1;

static SharedTex g_color, g_depth, g_motion, g_output;
static char g_versionName[128] = "";

// ---------------------------------------------------------------------------------------------
// Utilita'
// ---------------------------------------------------------------------------------------------
static std::wstring ModuleDirectory()
{
    HMODULE self = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCWSTR>(&ModuleDirectory), &self);
    wchar_t path[MAX_PATH] = {};
    GetModuleFileNameW(self, path, MAX_PATH);
    std::wstring p(path);
    size_t slash = p.find_last_of(L"\\/");
    return slash == std::wstring::npos ? L"." : p.substr(0, slash);
}

// Unity crea spesso le RenderTexture con formati "typeless": per FSR serve il formato tipizzato.
static DXGI_FORMAT TypedFormat(DXGI_FORMAT f)
{
    switch (f)
    {
    case DXGI_FORMAT_R16G16B16A16_TYPELESS: return DXGI_FORMAT_R16G16B16A16_FLOAT;
    case DXGI_FORMAT_R32G32B32A32_TYPELESS: return DXGI_FORMAT_R32G32B32A32_FLOAT;
    case DXGI_FORMAT_R8G8B8A8_TYPELESS:     return DXGI_FORMAT_R8G8B8A8_UNORM;
    case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:   return DXGI_FORMAT_R8G8B8A8_UNORM;
    case DXGI_FORMAT_B8G8R8A8_TYPELESS:     return DXGI_FORMAT_B8G8R8A8_UNORM;
    case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:   return DXGI_FORMAT_B8G8R8A8_UNORM;
    case DXGI_FORMAT_R10G10B10A2_TYPELESS:  return DXGI_FORMAT_R10G10B10A2_UNORM;
    case DXGI_FORMAT_R16G16_TYPELESS:       return DXGI_FORMAT_R16G16_FLOAT;
    case DXGI_FORMAT_R32G32_TYPELESS:       return DXGI_FORMAT_R32G32_FLOAT;
    case DXGI_FORMAT_R32_TYPELESS:          return DXGI_FORMAT_R32_FLOAT;
    case DXGI_FORMAT_R16_TYPELESS:          return DXGI_FORMAT_R16_FLOAT;
    default:                                return f;
    }
}

static D3D11_TEXTURE2D_DESC DescOf(void* tex)
{
    D3D11_TEXTURE2D_DESC d = {};
    static_cast<ID3D11Texture2D*>(tex)->GetDesc(&d);
    return d;
}

static void WaitForGpuIdle()
{
    if (!g_queue || !g_fence12) return;
    g_fenceValue++;
    g_queue->Signal(g_fence12.Get(), g_fenceValue);
    if (g_fence12->GetCompletedValue() < g_fenceValue)
    {
        g_fence12->SetEventOnCompletion(g_fenceValue, g_fenceEvent);
        WaitForSingleObject(g_fenceEvent, 5000);
    }
}

static void ReleaseShared(SharedTex& t)
{
    t.t11.Reset();
    t.r12.Reset();
    t.w = t.h = 0;
}

static void ReleaseContextAndResources()
{
    WaitForGpuIdle();
    if (g_ffxContext && g_ffx.DestroyContext)
    {
        g_ffx.DestroyContext(&g_ffxContext, nullptr);
        g_ffxContext = nullptr;
    }
    ReleaseShared(g_color);
    ReleaseShared(g_depth);
    ReleaseShared(g_motion);
    ReleaseShared(g_output);
    g_ctxRenderW = g_ctxRenderH = g_ctxDisplayW = g_ctxDisplayH = 0;
    g_ctxFlags = -1;
}

// ---------------------------------------------------------------------------------------------
// Inizializzazione dei dispositivi (una volta sola)
// ---------------------------------------------------------------------------------------------
static bool InitDevices(ID3D11Resource* anyGameTexture)
{
    if (g_deviceReady) return true;
    if (g_deviceFailed) return false;
    g_deviceFailed = true; // diventa false solo se tutto va a buon fine

    // 1) Dispositivo DX11 del gioco, ricavato da una sua texture
    ComPtr<ID3D11Device> dev11;
    anyGameTexture->GetDevice(&dev11);
    if (!dev11 || FAILED(dev11.As(&g_dev11)))
    {
        Log("DX11: ID3D11Device5 non disponibile (serve Windows 10 1703 o successivo).");
        g_status = -1;
        return false;
    }
    ComPtr<ID3D11DeviceContext> ctx;
    g_dev11->GetImmediateContext(&ctx);
    if (FAILED(ctx.As(&g_ctx11)))
    {
        Log("DX11: ID3D11DeviceContext4 non disponibile.");
        g_status = -1;
        return false;
    }

    // 2) Stessa scheda video per il dispositivo DX12
    ComPtr<IDXGIDevice> dxgiDevice;
    ComPtr<IDXGIAdapter> adapter;
    if (FAILED(g_dev11.As(&dxgiDevice)) || FAILED(dxgiDevice->GetAdapter(&adapter)))
    {
        Log("DXGI: impossibile ottenere la scheda video del gioco.");
        g_status = -2;
        return false;
    }
    DXGI_ADAPTER_DESC adesc = {};
    adapter->GetDesc(&adesc);
    char gpuName[128];
    WideCharToMultiByte(CP_UTF8, 0, adesc.Description, -1, gpuName, sizeof(gpuName), nullptr, nullptr);

    HRESULT hr = D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&g_dev12));
    if (FAILED(hr))
    {
        Log("DX12: creazione del dispositivo fallita (0x%08X) su %s.", (unsigned)hr, gpuName);
        g_status = -3;
        return false;
    }

    D3D12_COMMAND_QUEUE_DESC qd = {};
    qd.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    if (FAILED(g_dev12->CreateCommandQueue(&qd, IID_PPV_ARGS(&g_queue))))
    {
        Log("DX12: creazione della coda fallita.");
        g_status = -3;
        return false;
    }
    for (int i = 0; i < kAllocators; i++)
    {
        if (FAILED(g_dev12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&g_alloc[i]))))
        {
            Log("DX12: creazione del command allocator fallita.");
            g_status = -3;
            return false;
        }
    }
    if (FAILED(g_dev12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, g_alloc[0].Get(), nullptr, IID_PPV_ARGS(&g_list))))
    {
        Log("DX12: creazione della command list fallita.");
        g_status = -3;
        return false;
    }
    g_list->Close();

    // 3) Fence condivisa fra DX12 e DX11
    if (FAILED(g_dev12->CreateFence(0, D3D12_FENCE_FLAG_SHARED, IID_PPV_ARGS(&g_fence12))))
    {
        Log("DX12: creazione della fence condivisa fallita.");
        g_status = -4;
        return false;
    }
    HANDLE fenceHandle = nullptr;
    if (FAILED(g_dev12->CreateSharedHandle(g_fence12.Get(), nullptr, GENERIC_ALL, nullptr, &fenceHandle)) ||
        FAILED(g_dev11->OpenSharedFence(fenceHandle, IID_PPV_ARGS(&g_fence11))))
    {
        if (fenceHandle) CloseHandle(fenceHandle);
        Log("DX11: apertura della fence condivisa fallita.");
        g_status = -4;
        return false;
    }
    CloseHandle(fenceHandle);
    g_fenceEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);

    // 4) DLL ufficiali AMD (loader + upscaler) dalla cartella della mod
    std::wstring loaderPath = ModuleDirectory() + L"\\amd_fidelityfx_loader_dx12.dll";
    g_ffxModule = LoadLibraryExW(loaderPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!g_ffxModule)
    {
        Log("FFX: amd_fidelityfx_loader_dx12.dll non trovata accanto al plugin (errore %lu).", GetLastError());
        g_status = -5;
        return false;
    }
    ffxLoadFunctions(&g_ffx, g_ffxModule);
    if (!g_ffx.CreateContext || !g_ffx.Dispatch || !g_ffx.Query)
    {
        Log("FFX: funzioni dell'API non trovate nel loader.");
        g_status = -5;
        return false;
    }

    // 5) Versioni disponibili (incluse quelle fornite dal driver, marcate con *)
    ffxQueryDescGetVersions vq = {};
    vq.header.type = FFX_API_QUERY_DESC_TYPE_GET_VERSIONS;
    vq.createDescType = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
    vq.device = g_dev12.Get();
    uint64_t count = 0;
    vq.outputCount = &count;
    if (g_ffx.Query(nullptr, &vq.header) == FFX_API_RETURN_OK && count > 0)
    {
        std::vector<uint64_t> ids(count);
        std::vector<const char*> names(count);
        vq.versionIds = ids.data();
        vq.versionNames = names.data();
        if (g_ffx.Query(nullptr, &vq.header) == FFX_API_RETURN_OK)
        {
            std::string list;
            for (uint64_t i = 0; i < count; i++)
            {
                if (i) list += ", ";
                list += names[i] ? names[i] : "?";
            }
            Log("Upscaler disponibili su %s: %s", gpuName, list.c_str());
        }
    }

    g_deviceReady = true;
    g_deviceFailed = false;
    Log("Ponte DX11 -> DX12 pronto su %s.", gpuName);
    return true;
}

// ---------------------------------------------------------------------------------------------
// Texture condivise e contesto FSR (ricreati quando cambiano risoluzione o flag)
// ---------------------------------------------------------------------------------------------
// Le texture condivise vengono create dal lato DirectX 11 (handle NT) e aperte da DirectX 12:
// e' la direzione supportata in modo affidabile dai driver (DX12 -> DX11 restituiva E_INVALIDARG).
static bool CreateShared(SharedTex& t, int w, int h, DXGI_FORMAT format, bool uav, const char* name)
{
    D3D11_TEXTURE2D_DESC td = {};
    td.Width = (UINT)w;
    td.Height = (UINT)h;
    td.MipLevels = 1;
    td.ArraySize = 1;
    td.Format = format;
    td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT;
    td.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    if (uav) td.BindFlags |= D3D11_BIND_UNORDERED_ACCESS;
    td.MiscFlags = D3D11_RESOURCE_MISC_SHARED | D3D11_RESOURCE_MISC_SHARED_NTHANDLE;

    HRESULT hr = g_dev11->CreateTexture2D(&td, nullptr, &t.t11);
    if (FAILED(hr))
    {
        Log("DX11: creazione texture condivisa '%s' %dx%d formato %d fallita (0x%08X).", name, w, h, (int)format, (unsigned)hr);
        return false;
    }

    ComPtr<IDXGIResource1> dxgiRes;
    HANDLE handle = nullptr;
    hr = t.t11.As(&dxgiRes);
    if (SUCCEEDED(hr))
        hr = dxgiRes->CreateSharedHandle(nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE, nullptr, &handle);
    if (FAILED(hr))
    {
        Log("DX11: CreateSharedHandle '%s' fallita (0x%08X).", name, (unsigned)hr);
        t.t11.Reset();
        return false;
    }

    hr = g_dev12->OpenSharedHandle(handle, IID_PPV_ARGS(&t.r12));
    CloseHandle(handle);
    if (FAILED(hr))
    {
        Log("DX12: OpenSharedHandle '%s' fallita (0x%08X).", name, (unsigned)hr);
        t.t11.Reset();
        return false;
    }
    t.format = format;
    t.w = w;
    t.h = h;
    return true;
}

static bool SameShape(const SharedTex& t, const D3D11_TEXTURE2D_DESC& d)
{
    return t.t11 && t.w == (int)d.Width && t.h == (int)d.Height && t.format == TypedFormat(d.Format);
}

static bool EnsureContext(const FrameParams* p)
{
    D3D11_TEXTURE2D_DESC dc = DescOf(p->color), dd = DescOf(p->depth), dm = DescOf(p->motion), dout = DescOf(p->output);

    if (g_ffxContext && p->renderW == g_ctxRenderW && p->renderH == g_ctxRenderH &&
        p->displayW == g_ctxDisplayW && p->displayH == g_ctxDisplayH && p->createFlags == g_ctxFlags &&
        SameShape(g_color, dc) && SameShape(g_depth, dd) && SameShape(g_motion, dm) && SameShape(g_output, dout))
        return true;

    ReleaseContextAndResources();

    if (dc.SampleDesc.Count > 1 || (int)dc.Width < p->renderW || (int)dc.Height < p->renderH ||
        (int)dout.Width < p->displayW || (int)dout.Height < p->displayH)
    {
        Log("Texture di ingresso non valide (colore %ux%u x%u campioni, uscita %ux%u).",
            dc.Width, dc.Height, dc.SampleDesc.Count, dout.Width, dout.Height);
        g_status = -6;
        return false;
    }

    if (!CreateShared(g_color, (int)dc.Width, (int)dc.Height, TypedFormat(dc.Format), false, "colore") ||
        !CreateShared(g_depth, (int)dd.Width, (int)dd.Height, TypedFormat(dd.Format), false, "profondita'") ||
        !CreateShared(g_motion, (int)dm.Width, (int)dm.Height, TypedFormat(dm.Format), false, "movimento") ||
        !CreateShared(g_output, (int)dout.Width, (int)dout.Height, TypedFormat(dout.Format), true, "uscita"))
    {
        ReleaseContextAndResources();
        g_status = -6;
        return false;
    }

    ffxCreateBackendDX12Desc backend = {};
    backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
    backend.device = g_dev12.Get();

    ffxCreateContextDescUpscale create = {};
    create.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
    create.header.pNext = &backend.header;
    create.flags = (uint32_t)p->createFlags;
    create.maxRenderSize.width = (uint32_t)p->renderW;
    create.maxRenderSize.height = (uint32_t)p->renderH;
    create.maxUpscaleSize.width = (uint32_t)p->displayW;
    create.maxUpscaleSize.height = (uint32_t)p->displayH;
    create.fpMessage = FfxMessageCallback;

    ffxReturnCode_t rc = g_ffx.CreateContext(&g_ffxContext, &create.header, nullptr);
    if (rc != FFX_API_RETURN_OK || !g_ffxContext)
    {
        Log("FFX: creazione del contesto di upscaling fallita (codice %u).", rc);
        g_ffxContext = nullptr;
        ReleaseContextAndResources();
        g_status = -7;
        return false;
    }

    ffxQueryGetProviderVersion ver = {};
    ver.header.type = FFX_API_QUERY_DESC_TYPE_GET_PROVIDER_VERSION;
    if (g_ffx.Query(&g_ffxContext, &ver.header) == FFX_API_RETURN_OK && ver.versionName)
        strncpy_s(g_versionName, ver.versionName, _TRUNCATE);
    else
        strcpy_s(g_versionName, "sconosciuta");

    g_ctxRenderW = p->renderW;
    g_ctxRenderH = p->renderH;
    g_ctxDisplayW = p->displayW;
    g_ctxDisplayH = p->displayH;
    g_ctxFlags = p->createFlags;
    g_status = 1;
    Log("Contesto creato: %s, %dx%d -> %dx%d, flag 0x%X, formati colore %d / uscita %d.", g_versionName,
        p->renderW, p->renderH, p->displayW, p->displayH, p->createFlags, (int)g_color.format, (int)g_output.format);
    return true;
}

static void Transition(ID3D12Resource* r, D3D12_RESOURCE_STATES from, D3D12_RESOURCE_STATES to, std::vector<D3D12_RESOURCE_BARRIER>& out)
{
    D3D12_RESOURCE_BARRIER b = {};
    b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition.pResource = r;
    b.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    b.Transition.StateBefore = from;
    b.Transition.StateAfter = to;
    out.push_back(b);
}

// ---------------------------------------------------------------------------------------------
// Frame
// ---------------------------------------------------------------------------------------------
static void RunFrame(const FrameParams* p)
{
    if (!p || !p->color || !p->depth || !p->motion || !p->output) return;
    if (!InitDevices(static_cast<ID3D11Resource*>(p->color))) return;
    if (!EnsureContext(p)) return;

    // 1) DX11 -> texture condivise
    g_ctx11->CopyResource(g_color.t11.Get(), static_cast<ID3D11Resource*>(p->color));
    g_ctx11->CopyResource(g_depth.t11.Get(), static_cast<ID3D11Resource*>(p->depth));
    g_ctx11->CopyResource(g_motion.t11.Get(), static_cast<ID3D11Resource*>(p->motion));
    g_fenceValue++;
    g_ctx11->Signal(g_fence11.Get(), g_fenceValue);
    g_ctx11->Flush();

    // 2) DX12: attende DX11 sulla GPU ed esegue FSR
    g_queue->Wait(g_fence12.Get(), g_fenceValue);

    int slot = g_frameIndex % kAllocators;
    if (g_fence12->GetCompletedValue() < g_allocFence[slot])
    {
        g_fence12->SetEventOnCompletion(g_allocFence[slot], g_fenceEvent);
        WaitForSingleObject(g_fenceEvent, 1000);
    }
    g_alloc[slot]->Reset();
    g_list->Reset(g_alloc[slot].Get(), nullptr);

    std::vector<D3D12_RESOURCE_BARRIER> barriers;
    Transition(g_color.r12.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, barriers);
    Transition(g_depth.r12.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, barriers);
    Transition(g_motion.r12.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, barriers);
    Transition(g_output.r12.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, barriers);
    g_list->ResourceBarrier((UINT)barriers.size(), barriers.data());

    ffxDispatchDescUpscale d = {};
    d.header.type = FFX_API_DISPATCH_DESC_TYPE_UPSCALE;
    d.commandList = g_list.Get();
    d.color = ffxApiGetResourceDX12(g_color.r12.Get(), FFX_API_RESOURCE_STATE_COMPUTE_READ);
    d.depth = ffxApiGetResourceDX12(g_depth.r12.Get(), FFX_API_RESOURCE_STATE_COMPUTE_READ);
    d.motionVectors = ffxApiGetResourceDX12(g_motion.r12.Get(), FFX_API_RESOURCE_STATE_COMPUTE_READ);
    d.output = ffxApiGetResourceDX12(g_output.r12.Get(), FFX_API_RESOURCE_STATE_UNORDERED_ACCESS);
    d.jitterOffset.x = p->jitterX;
    d.jitterOffset.y = p->jitterY;
    d.motionVectorScale.x = p->mvScaleX;
    d.motionVectorScale.y = p->mvScaleY;
    d.renderSize.width = (uint32_t)p->renderW;
    d.renderSize.height = (uint32_t)p->renderH;
    d.upscaleSize.width = (uint32_t)p->displayW;
    d.upscaleSize.height = (uint32_t)p->displayH;
    d.enableSharpening = p->enableSharpening != 0;
    d.sharpness = p->sharpness;
    d.frameTimeDelta = p->frameTimeMs;
    d.preExposure = 1.0f;
    d.reset = p->reset != 0;
    d.cameraNear = p->cameraNear;
    d.cameraFar = p->cameraFar;
    d.cameraFovAngleVertical = p->fovY;
    d.viewSpaceToMetersFactor = 1.0f;
    d.flags = 0;

    ffxReturnCode_t rc = g_ffx.Dispatch(&g_ffxContext, &d.header);
    if (rc != FFX_API_RETURN_OK)
    {
        static int errorsLogged = 0;
        if (errorsLogged++ < 5) Log("FFX: dispatch fallito (codice %u).", rc);
    }

    barriers.clear();
    Transition(g_color.r12.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON, barriers);
    Transition(g_depth.r12.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON, barriers);
    Transition(g_motion.r12.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON, barriers);
    Transition(g_output.r12.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COMMON, barriers);
    g_list->ResourceBarrier((UINT)barriers.size(), barriers.data());
    g_list->Close();

    ID3D12CommandList* lists[] = { g_list.Get() };
    g_queue->ExecuteCommandLists(1, lists);
    g_fenceValue++;
    g_queue->Signal(g_fence12.Get(), g_fenceValue);
    g_allocFence[slot] = g_fenceValue;
    g_frameIndex++;

    // 3) DX11: attende DX12 sulla GPU e copia il risultato per Unity
    g_ctx11->Wait(g_fence11.Get(), g_fenceValue);
    g_ctx11->CopyResource(static_cast<ID3D11Resource*>(p->output), g_output.t11.Get());
}

static void __stdcall OnRenderEvent(int eventId, void* data)
{
    switch (eventId)
    {
    case EventFrame:
        RunFrame(static_cast<const FrameParams*>(data));
        break;
    case EventRelease:
        if (g_deviceReady) ReleaseContextAndResources();
        g_status = 0;
        break;
    }
}

// ---------------------------------------------------------------------------------------------
// Funzioni esportate verso C#
// ---------------------------------------------------------------------------------------------
OFSR_EXPORT void* OFSR4_GetRenderEventFunc() { return reinterpret_cast<void*>(&OnRenderEvent); }

OFSR_EXPORT int OFSR4_GetStatus() { return (int)g_status; }

OFSR_EXPORT int OFSR4_GetVersionName(char* buffer, int size)
{
    if (!buffer || size <= 0) return 0;
    strncpy_s(buffer, (size_t)size, g_versionName, _TRUNCATE);
    return (int)strlen(buffer);
}

OFSR_EXPORT int OFSR4_PopLog(char* buffer, int size)
{
    if (!buffer || size <= 0) return 0;
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (g_log.empty()) return 0;
    strncpy_s(buffer, (size_t)size, g_log.front().c_str(), _TRUNCATE);
    g_log.pop_front();
    return (int)strlen(buffer);
}

OFSR_EXPORT int OFSR4_GetParamsSize() { return (int)sizeof(FrameParams); }
