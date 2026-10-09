// ==WindhawkMod==
// @id              ghostwriter-blur
// @name            Ghostwriter Blur
// @description     Blur (WindhawkBlur-style) and freely rounded corners (any radius, also on Acrylic/Mica) for the Ghostwriter overlay, each with its own switch
// @version         0.2.0
// @author          T
// @include         Ghostwriter.exe
// @include         dwm.exe
// @architecture    x86-64
// @compilerOptions -lruntimeobject -lole32 -loleaut32 -ldwmapi -luser32 -lgdi32 -ladvapi32 -lwevtapi
// @license         GPL-3.0
// ==/WindhawkMod==

// License: the corner part (the hooks in dwm.exe) follows the approach and the symbol list of "Custom Window Corner
// Radius" by m417z (GPL-3.0, https://github.com/ramensoftware/windhawk-mods), which is why this file is GPL-3.0 as a whole.
// The rest of Ghostwriter is MIT.

// ==WindhawkModReadme==
/*
# Ghostwriter Blur

Two independent features, each with its own switch (**Enable blur**, **Enable rounded corners**):

* **Blur**: a WindhawkBlur-style backdrop blur behind the overlay, for Ghostwriter windows without a system material
  (`blur = "none"` in Ghostwriter). Runs inside Ghostwriter.exe.
* **Rounded corners**: a corner radius of your choice for Ghostwriter windows that have a system material (`acrylic`,
  `blur`, `mica`, `micaalt`). Windows only offers 0, 4 and 8 px there; the mod makes the window manager (dwm.exe) use the radius
  of Ghostwriter instead, material included. See **Rounded corners** below.

## Blur

Gives the [Ghostwriter](https://github.com/) overlay (and its status pill) the same kind of backdrop blur that
Windhawk's `WindhawkBlur` brush gives the taskbar and the Start menu: a Gaussian blur of whatever is behind the window,
with adjustable strength, tint, saturation, luminosity and noise, clipped to **any** corner radius, anti-aliased.

Ghostwriter itself does no blur work for this. The mod puts a small click-through window directly *below* the overlay;
that window shows a `Windows.UI.Composition` effect (host backdrop -> Gaussian blur -> tint), and the system draws it.
The helper window never takes the focus, never receives mouse input and follows the overlay (show, hide, move, resize).

## Setup

1. In Ghostwriter's `settings.toml` set `blur = "none"` under `[appearance]`. The overlay is then a translucent
   surface, and `transparency` decides how much of the blur shows through (for example `transparency = 40`).
2. Enable this mod. The corner radius follows `radius` from the same `[appearance]` block (set **CornerRadius** below to
   override it). With `blur = "none"` Ghostwriter honours the radius exactly, so the surface and the blur have the same shape.

With any other `blur` value in Ghostwriter (acrylic, blur, mica, micaalt) the app already has a system material, and
the mod stays out of the way (see **Only when the app's blur is "none"**).

## Rounded corners

Windows 11 cuts a window and its material (Acrylic, Mica) to a corner radius of 0, 4 or 8 px only. This mod hooks the
corner radius in `udwm.dll` (inside `dwm.exe`) and returns Ghostwriter's radius for Ghostwriter windows. Other windows are
not touched: a window counts as Ghostwriter's only if its class starts with `HwndWrapper[Ghostwriter` and the app has marked it
with the window property `Ghostwriter.CornerRadius`.

1. In Ghostwriter's `settings.toml` set `custom_corners = true` under `[appearance]` (and keep `blur` at `acrylic`, `blur`,
   `mica` or `micaalt`). The app then asks Windows to round the window, draws its border with the exact radius and publishes the
   radius for the mod.
2. Enable this mod. The radius follows `radius` from the same block (set **CornerRadius** below to override it).
3. The radius is applied when a window is shown, so changes show up the next time the overlay opens.

Notes: needs a Windows 11 build that has `GetRadiusFromCornerStyle` in `udwm.dll` (24H2 or later is tested by the original
mod); if the symbols are not found the mod logs it and does nothing. The mod refuses to load right after the window manager
restarted twice within a minute. A radius larger than half of a window's height (the small status pill) is rounded as far as the
system allows. If nothing happens, check in Windhawk's advanced settings that `dwm.exe` is allowed as a target process.

## Settings

* **Enable blur**, **Enable rounded corners**: the two switches; each feature works without the other.
* **Blur amount**: strength of the Gaussian blur (standard deviation, like `BlurAmount` of `WindhawkBlur`).
* **Tint color / Tint opacity**: a color laid over the blur. The default is no tint, because Ghostwriter's own
  `background` and `transparency` already tint the surface.
* **Tint luminosity opacity**, **Tint saturation**, **Noise opacity**, **Noise density**: as in `WindhawkBlur`.
* **Corner radius**: in pixels at 100% scaling; -1 follows Ghostwriter's `radius`. Used for the blur's shape and for the rounded corners.

## Notes

* Windows 11 22H2 or later. The blur comes from the system's host backdrop; when "Transparency effects" are off in
  Windows, the system shows its own flat fallback color.
* x86-64 only. The effect code is based on the `XamlBlurBrush` of the TranslucentTB project, as used by the Windhawk
  Taskbar and Start menu stylers.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- EnableBlur: true
  $name: Enable blur
  $description: >-
    The WindhawkBlur-style blur behind Ghostwriter windows that have no system material (blur = "none" in Ghostwriter).
- EnableCorners: true
  $name: Enable rounded corners
  $description: >-
    Freely rounded corners for Ghostwriter windows with a system material (needs custom_corners = true in Ghostwriter).
    Works inside dwm.exe.
- BlurAmount: 30
  $name: Blur amount
  $description: Strength of the Gaussian blur. 0 = no blur.
- TintColor: "#000000"
  $name: Tint color
  $description: "Color laid over the blur, as #RRGGBB."
- TintOpacity: 0
  $name: Tint opacity (%)
  $description: 0-100. The default is 0 because Ghostwriter's own surface already provides the tint.
- TintLuminosityOpacity: 0
  $name: Tint luminosity opacity (%)
  $description: 0-100. Shifts the luminosity of the blurred backdrop towards the tint's luminosity.
- TintSaturation: 100
  $name: Tint saturation (%)
  $description: 100 leaves the backdrop's saturation unchanged, 0 makes it grey, above 100 boosts it.
- NoiseOpacity: 0
  $name: Noise opacity (%)
  $description: 0-100. Adds a fine noise texture over the blur, like Windows acrylic.
- NoiseDensity: 100
  $name: Noise density (%)
  $description: 1-100.
- CornerRadius: -1
  $name: Corner radius (px)
  $description: >-
    Corner radius in pixels at 100% scaling. -1 follows the "radius" setting of Ghostwriter's settings.toml.
- OnlyWhenAppBlurIsNone: true
  $name: Only when the app's blur is "none"
  $description: >-
    Draw the blur only when Ghostwriter's settings.toml has blur = "none". Otherwise the app already shows a system
    material and a second blur would only add cost. Turn this off to always draw the blur.
- ApplyToStatusPill: true
  $name: Apply to the status pill
  $description: Also blur the small status/progress pill.
*/
// ==/WindhawkModSettings==

#include <windhawk_utils.h>

#include <windows.h>
#include <initguid.h>
#include <dwmapi.h>
#include <winevt.h>
#include <d2d1_1.h>
#include <roapi.h>
#include <winstring.h>
#include <windows.graphics.effects.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <limits>
#include <mutex>
#include <random>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

#ifdef GetCurrentTime
#undef GetCurrentTime  // the Windows.h macro collides with a XAML method name
#endif

#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <winrt/Windows.UI.Xaml.Media.h>

namespace wf = winrt::Windows::Foundation;
namespace wge = winrt::Windows::Graphics::Effects;
namespace wuc = winrt::Windows::UI::Composition;
namespace awge = ABI::Windows::Graphics::Effects;

////////////////////////////////////////////////////////////////////////////////
// Effect graph building blocks (Gaussian blur, color matrix, composite, flood, border), copied from the
// XamlBlurBrush of the TranslucentTB project as used by the Windhawk Taskbar Styler mod.

// clang-format off
template <> inline constexpr winrt::guid winrt::impl::guid_v<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>{
    winrt::impl::guid_v<winrt::Windows::Foundation::IPropertyValue>
};

typedef enum MY_D2D1_GAUSSIANBLUR_OPTIMIZATION
{
    MY_D2D1_GAUSSIANBLUR_OPTIMIZATION_SPEED = 0,
    MY_D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED = 1,
    MY_D2D1_GAUSSIANBLUR_OPTIMIZATION_QUALITY = 2,
    MY_D2D1_GAUSSIANBLUR_OPTIMIZATION_FORCE_DWORD = 0xffffffff

} MY_D2D1_GAUSSIANBLUR_OPTIMIZATION;

////////////////////////////////////////////////////////////////////////////////

// windows.graphics.effects.interop.h
#ifndef BUILD_WINDOWS
namespace ABI {
#endif
namespace Windows {
namespace Graphics {
namespace Effects {

typedef interface IGraphicsEffectSource                         IGraphicsEffectSource;
typedef interface IGraphicsEffectD2D1Interop                    IGraphicsEffectD2D1Interop;


typedef enum GRAPHICS_EFFECT_PROPERTY_MAPPING
{
    GRAPHICS_EFFECT_PROPERTY_MAPPING_UNKNOWN,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_VECTORX,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_VECTORY,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_VECTORZ,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_VECTORW,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_RECT_TO_VECTOR4,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_RADIANS_TO_DEGREES,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_COLORMATRIX_ALPHA_MODE,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_COLOR_TO_VECTOR3,
    GRAPHICS_EFFECT_PROPERTY_MAPPING_COLOR_TO_VECTOR4
} GRAPHICS_EFFECT_PROPERTY_MAPPING;

//+-----------------------------------------------------------------------------
//
//  Interface:
//      IGraphicsEffectD2D1Interop
//
//  Synopsis:
//      An interface providing a Interop counterpart to IGraphicsEffect
//      and allowing for metadata queries.
//
//------------------------------------------------------------------------------

#undef INTERFACE
#define INTERFACE IGraphicsEffectD2D1Interop
DECLARE_INTERFACE_IID_(IGraphicsEffectD2D1Interop, IUnknown, "2FC57384-A068-44D7-A331-30982FCF7177")
{
    STDMETHOD(GetEffectId)(
        _Out_ GUID * id
        ) PURE;

    STDMETHOD(GetNamedPropertyMapping)(
        LPCWSTR name,
        _Out_ UINT * index,
        _Out_ GRAPHICS_EFFECT_PROPERTY_MAPPING * mapping
        ) PURE;

    STDMETHOD(GetPropertyCount)(
        _Out_ UINT * count
        ) PURE;

    STDMETHOD(GetProperty)(
        UINT index,
        _Outptr_ winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue> ** value
        ) PURE;

    STDMETHOD(GetSource)(
        UINT index,
        _Outptr_ IGraphicsEffectSource ** source
        ) PURE;

    STDMETHOD(GetSourceCount)(
        _Out_ UINT * count
        ) PURE;
};


} // namespace Effects
} // namespace Graphics
} // namespace Windows
#ifndef BUILD_WINDOWS
} // namespace ABI
#endif

template <> inline constexpr winrt::guid winrt::impl::guid_v<ABI::Windows::Graphics::Effects::IGraphicsEffectD2D1Interop>{
    0x2FC57384, 0xA068, 0x44D7, { 0xA3, 0x31, 0x30, 0x98, 0x2F, 0xCF, 0x71, 0x77 }
};



////////////////////////////////////////////////////////////////////////////////
// CompositeEffect.h
struct CompositeEffect : winrt::implements<CompositeEffect, wge::IGraphicsEffect, wge::IGraphicsEffectSource, awge::IGraphicsEffectD2D1Interop>
{
public:
    // IGraphicsEffectD2D1Interop
    HRESULT STDMETHODCALLTYPE GetEffectId(GUID* id) noexcept override;
    HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept override;
    HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT* count) noexcept override;
    HRESULT STDMETHODCALLTYPE GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSourceCount(UINT* count) noexcept override;

    // IGraphicsEffect
    winrt::hstring Name();
    void Name(winrt::hstring name);

    std::vector<wge::IGraphicsEffectSource> Sources;
    D2D1_COMPOSITE_MODE Mode = D2D1_COMPOSITE_MODE_SOURCE_OVER;
private:
    winrt::hstring m_name = L"CompositeEffect";
};

////////////////////////////////////////////////////////////////////////////////
// CompositeEffect.cpp
HRESULT CompositeEffect::GetEffectId(GUID* id) noexcept
{
    if (id == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *id = CLSID_D2D1Composite;
    return S_OK;
}

HRESULT CompositeEffect::GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept
{
    if (index == nullptr || mapping == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    const std::wstring_view nameView(name);
    if (nameView == L"Mode")
    {
        *index = D2D1_COMPOSITE_PROP_MODE;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }

    return E_INVALIDARG;
}

HRESULT CompositeEffect::GetPropertyCount(UINT* count) noexcept
{
    if (count == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *count = 1;
    return S_OK;
}

HRESULT CompositeEffect::GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept try
{
    if (value == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    switch (index)
    {
        case D2D1_COMPOSITE_PROP_MODE:
            *value = wf::PropertyValue::CreateUInt32((UINT32)Mode).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        default:
            return E_BOUNDS;
    }

    return S_OK;
}
catch (...)
{
    return winrt::to_hresult();
}

HRESULT CompositeEffect::GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept try
{
    if (source == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    winrt::copy_to_abi(Sources.at(index), *reinterpret_cast<void**>(source));
    return S_OK;
}
catch (...)
{
    return winrt::to_hresult();
}

HRESULT CompositeEffect::GetSourceCount(UINT* count) noexcept
{
    if (count == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *count = static_cast<UINT>(Sources.size());
    return S_OK;
}

winrt::hstring CompositeEffect::Name()
{
    return m_name;
}

void CompositeEffect::Name(winrt::hstring name)
{
    m_name = name;
}

////////////////////////////////////////////////////////////////////////////////
// FloodEffect.h
struct FloodEffect : winrt::implements<FloodEffect, wge::IGraphicsEffect, wge::IGraphicsEffectSource, awge::IGraphicsEffectD2D1Interop>
{
public:
    // IGraphicsEffectD2D1Interop
    HRESULT STDMETHODCALLTYPE GetEffectId(GUID* id) noexcept override;
    HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept override;
    HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT* count) noexcept override;
    HRESULT STDMETHODCALLTYPE GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSourceCount(UINT* count) noexcept override;

    // IGraphicsEffect
    winrt::hstring Name();
    void Name(winrt::hstring name);

    winrt::Windows::UI::Color Color{};
private:
    winrt::hstring m_name = L"FloodEffect";
};

////////////////////////////////////////////////////////////////////////////////
// FloodEffect.cpp
HRESULT FloodEffect::GetEffectId(GUID* id) noexcept
{
    if (id == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *id = CLSID_D2D1Flood;
    return S_OK;
}

HRESULT FloodEffect::GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept
{
    if (index == nullptr || mapping == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    const std::wstring_view nameView(name);
    if (nameView == L"Color")
    {
        *index = D2D1_FLOOD_PROP_COLOR;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }

    return E_INVALIDARG;
}

HRESULT FloodEffect::GetPropertyCount(UINT* count) noexcept
{
    if (count == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *count = 1;
    return S_OK;
}

HRESULT FloodEffect::GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept try
{
    if (value == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    switch (index)
    {
        case D2D1_FLOOD_PROP_COLOR:
            *value = wf::PropertyValue::CreateSingleArray({
                Color.R / 255.0f,
                Color.G / 255.0f,
                Color.B / 255.0f,
                Color.A / 255.0f,
            }).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        default:
            return E_BOUNDS;
    }

    return S_OK;
}
catch (...)
{
    return winrt::to_hresult();
}

HRESULT FloodEffect::GetSource(UINT, awge::IGraphicsEffectSource** source) noexcept
{
    if (source == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    return E_BOUNDS;
}

HRESULT FloodEffect::GetSourceCount(UINT* count) noexcept
{
    if (count == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *count = 0;
    return S_OK;
}

winrt::hstring FloodEffect::Name()
{
    return m_name;
}

void FloodEffect::Name(winrt::hstring name)
{
    m_name = name;
}

////////////////////////////////////////////////////////////////////////////////
// BorderEffect.h
struct BorderEffect : winrt::implements<BorderEffect, wge::IGraphicsEffect, wge::IGraphicsEffectSource, awge::IGraphicsEffectD2D1Interop>
{
public:
    // IGraphicsEffectD2D1Interop
    HRESULT STDMETHODCALLTYPE GetEffectId(GUID* id) noexcept override;
    HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept override;
    HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT* count) noexcept override;
    HRESULT STDMETHODCALLTYPE GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSourceCount(UINT* count) noexcept override;

    // IGraphicsEffect
    winrt::hstring Name();
    void Name(winrt::hstring name);

    wge::IGraphicsEffectSource Source{nullptr};
    D2D1_BORDER_EDGE_MODE ExtendX = D2D1_BORDER_EDGE_MODE_WRAP;
    D2D1_BORDER_EDGE_MODE ExtendY = D2D1_BORDER_EDGE_MODE_WRAP;
private:
    winrt::hstring m_name = L"BorderEffect";
};

////////////////////////////////////////////////////////////////////////////////
// BorderEffect.cpp
HRESULT BorderEffect::GetEffectId(GUID* id) noexcept
{
    if (!id)
    {
        return E_INVALIDARG;
    }

    *id = CLSID_D2D1Border;
    return S_OK;
}

HRESULT BorderEffect::GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept
{
    if (!index || !mapping)
    {
        return E_INVALIDARG;
    }

    const std::wstring_view nameView(name);
    if (nameView == L"ExtendX")
    {
        *index = D2D1_BORDER_PROP_EDGE_MODE_X;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }

    if (nameView == L"ExtendY")
    {
        *index = D2D1_BORDER_PROP_EDGE_MODE_Y;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }

    return E_INVALIDARG;
}

HRESULT BorderEffect::GetPropertyCount(UINT* count) noexcept
{
    if (!count)
    {
        return E_INVALIDARG;
    }

    *count = 2;
    return S_OK;
}

HRESULT BorderEffect::GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept try
{
    if (!value)
    {
        return E_INVALIDARG;
    }

    switch (index)
    {
        case D2D1_BORDER_PROP_EDGE_MODE_X:
            *value = wf::PropertyValue::CreateUInt32((UINT32)ExtendX).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        case D2D1_BORDER_PROP_EDGE_MODE_Y:
            *value = wf::PropertyValue::CreateUInt32((UINT32)ExtendY).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        default:
            return E_BOUNDS;
    }

    return S_OK;
}
catch (...)
{
    return winrt::to_hresult();
}

HRESULT BorderEffect::GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept
{
    if (!source)
    {
        return E_INVALIDARG;
    }

    if (index == 0 && Source)
    {
        winrt::copy_to_abi(Source, *reinterpret_cast<void**>(source));
        return S_OK;
    }

    return E_BOUNDS;
}

HRESULT BorderEffect::GetSourceCount(UINT* count) noexcept
{
    if (!count)
    {
        return E_INVALIDARG;
    }

    *count = 1;
    return S_OK;
}

winrt::hstring BorderEffect::Name()
{
    return m_name;
}

void BorderEffect::Name(winrt::hstring name)
{
    m_name = name;
}


////////////////////////////////////////////////////////////////////////////////
// GaussianBlurEffect.h
struct GaussianBlurEffect : winrt::implements<GaussianBlurEffect, wge::IGraphicsEffect, wge::IGraphicsEffectSource, awge::IGraphicsEffectD2D1Interop>
{
public:
    // IGraphicsEffectD2D1Interop
    HRESULT STDMETHODCALLTYPE GetEffectId(GUID* id) noexcept override;
    HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept override;
    HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT* count) noexcept override;
    HRESULT STDMETHODCALLTYPE GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSourceCount(UINT* count) noexcept override;

    // IGraphicsEffect
    winrt::hstring Name();
    void Name(winrt::hstring name);

    wge::IGraphicsEffectSource Source;

    float BlurAmount = 3.0f;
    MY_D2D1_GAUSSIANBLUR_OPTIMIZATION Optimization = MY_D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED;
    D2D1_BORDER_MODE BorderMode = D2D1_BORDER_MODE_SOFT;
private:
    winrt::hstring m_name = L"GaussianBlurEffect";
};

////////////////////////////////////////////////////////////////////////////////
// GaussianBlurEffect.cpp
HRESULT GaussianBlurEffect::GetEffectId(GUID* id) noexcept
{
    if (id == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *id = CLSID_D2D1GaussianBlur;
    return S_OK;
}

HRESULT GaussianBlurEffect::GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept
{
    if (index == nullptr || mapping == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    const std::wstring_view nameView(name);
    if (nameView == L"BlurAmount")
    {
        *index = D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }
    else if (nameView == L"Optimization")
    {
        *index = D2D1_GAUSSIANBLUR_PROP_OPTIMIZATION;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }
    else if (nameView == L"BorderMode")
    {
        *index = D2D1_GAUSSIANBLUR_PROP_BORDER_MODE;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }

    return E_INVALIDARG;
}

HRESULT GaussianBlurEffect::GetPropertyCount(UINT* count) noexcept
{
    if (count == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *count = 3;
    return S_OK;
}

HRESULT GaussianBlurEffect::GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept try
{
    if (value == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    switch (index)
    {
        case D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION:
            *value = wf::PropertyValue::CreateSingle(BlurAmount).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        case D2D1_GAUSSIANBLUR_PROP_OPTIMIZATION:
            *value = wf::PropertyValue::CreateUInt32((UINT32)Optimization).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        case D2D1_GAUSSIANBLUR_PROP_BORDER_MODE:
            *value = wf::PropertyValue::CreateUInt32((UINT32)BorderMode).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        default:
            return E_BOUNDS;
    }

    return S_OK;
}
catch (...)
{
    return winrt::to_hresult();
}

HRESULT GaussianBlurEffect::GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept
{
    if (source == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    if (index == 0)
    {
        winrt::copy_to_abi(Source, *reinterpret_cast<void**>(source));
        return S_OK;
    }
    else
    {
        return E_BOUNDS;
    }
}

HRESULT GaussianBlurEffect::GetSourceCount(UINT* count) noexcept
{
    if (count == nullptr) [[unlikely]]
    {
        return E_INVALIDARG;
    }

    *count = 1;
    return S_OK;
}

winrt::hstring GaussianBlurEffect::Name()
{
    return m_name;
}

void GaussianBlurEffect::Name(winrt::hstring name)
{
    m_name = name;
}

////////////////////////////////////////////////////////////////////////////////
// ColorMatrixEffect.h
struct ColorMatrixEffect : winrt::implements<ColorMatrixEffect, wge::IGraphicsEffect, wge::IGraphicsEffectSource, awge::IGraphicsEffectD2D1Interop>
{
public:
    // IGraphicsEffectD2D1Interop
    HRESULT STDMETHODCALLTYPE GetEffectId(GUID* id) noexcept override;
    HRESULT STDMETHODCALLTYPE GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept override;
    HRESULT STDMETHODCALLTYPE GetPropertyCount(UINT* count) noexcept override;
    HRESULT STDMETHODCALLTYPE GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept override;
    HRESULT STDMETHODCALLTYPE GetSourceCount(UINT* count) noexcept override;

    // IGraphicsEffect
    winrt::hstring Name();
    void Name(winrt::hstring name);

    wge::IGraphicsEffectSource Source{nullptr};

    // D2D1_MATRIX_5X4_F: 5 rows x 4 columns (20 floats), initialized to identity.
    float Matrix[20] = {
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
        0, 0, 0, 0,
    };

    uint32_t AlphaMode = D2D1_COLORMATRIX_ALPHA_MODE_PREMULTIPLIED;
    bool ClampOutput = false;
private:
    winrt::hstring m_name = L"ColorMatrixEffect";
};

////////////////////////////////////////////////////////////////////////////////
// ColorMatrixEffect.cpp
HRESULT ColorMatrixEffect::GetEffectId(GUID* id) noexcept
{
    if (!id)
    {
        return E_INVALIDARG;
    }

    *id = CLSID_D2D1ColorMatrix;
    return S_OK;
}

HRESULT ColorMatrixEffect::GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept
{
    if (!index || !mapping)
    {
        return E_INVALIDARG;
    }

    const std::wstring_view nameView(name);
    if (nameView == L"ColorMatrix")
    {
        *index = D2D1_COLORMATRIX_PROP_COLOR_MATRIX;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }

    if (nameView == L"AlphaMode")
    {
        *index = D2D1_COLORMATRIX_PROP_ALPHA_MODE;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }

    if (nameView == L"ClampOutput")
    {
        *index = D2D1_COLORMATRIX_PROP_CLAMP_OUTPUT;
        *mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;

        return S_OK;
    }

    return E_INVALIDARG;
}

HRESULT ColorMatrixEffect::GetPropertyCount(UINT* count) noexcept
{
    if (!count)
    {
        return E_INVALIDARG;
    }

    *count = 3;
    return S_OK;
}

HRESULT ColorMatrixEffect::GetProperty(UINT index, winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>** value) noexcept try
{
    if (!value)
    {
        return E_INVALIDARG;
    }

    switch (index)
    {
        case D2D1_COLORMATRIX_PROP_COLOR_MATRIX:
            *value = wf::PropertyValue::CreateSingleArray(
                winrt::array_view<const float>(Matrix, Matrix + 20)
            ).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        case D2D1_COLORMATRIX_PROP_ALPHA_MODE:
            *value = wf::PropertyValue::CreateUInt32(AlphaMode).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        case D2D1_COLORMATRIX_PROP_CLAMP_OUTPUT:
            *value = wf::PropertyValue::CreateBoolean(ClampOutput).as<winrt::impl::abi_t<winrt::Windows::Foundation::IPropertyValue>>().detach();
            break;

        default:
            return E_BOUNDS;
    }

    return S_OK;
}
catch (...)
{
    return winrt::to_hresult();
}

HRESULT ColorMatrixEffect::GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept
{
    if (!source)
    {
        return E_INVALIDARG;
    }

    if (index == 0 && Source)
    {
        winrt::copy_to_abi(Source, *reinterpret_cast<void**>(source));
        return S_OK;
    }

    return E_BOUNDS;
}

HRESULT ColorMatrixEffect::GetSourceCount(UINT* count) noexcept
{
    if (!count)
    {
        return E_INVALIDARG;
    }

    *count = 1;
    return S_OK;
}

winrt::hstring ColorMatrixEffect::Name()
{
    return m_name;
}

void ColorMatrixEffect::Name(winrt::hstring name)
{
    m_name = name;
}



////////////////////////////////////////////////////////////////////////////////
// COM interfaces declared locally, so the mod doesn't depend on optional SDK headers.

// ICompositorDesktopInterop
struct DesktopInterop : ::IUnknown {
    virtual HRESULT STDMETHODCALLTYPE CreateDesktopWindowTarget(HWND hwndTarget,
                                                                BOOL isTopmost,
                                                                void** result) = 0;
    virtual HRESULT STDMETHODCALLTYPE EnsureOnThread(DWORD threadId) = 0;
};
constexpr GUID kDesktopInteropIid = {
    0x29E691FA, 0x4567, 0x4DCA, {0xB3, 0x19, 0xD0, 0xF2, 0x07, 0xEB, 0x68, 0x07}};

// DispatcherQueueOptions / CreateDispatcherQueueController
struct DispatcherQueueOptionsAbi {
    DWORD dwSize;
    int threadType;
    int apartmentType;
};
constexpr int kDispatcherQueueThreadCurrent = 2;  // DQTYPE_THREAD_CURRENT
constexpr int kDispatcherQueueComSta = 2;         // DQTAT_COM_STA
using CreateDispatcherQueueController_t =
    HRESULT(WINAPI*)(DispatcherQueueOptionsAbi options, void** controller);

constexpr DWORD kDwmaUseHostBackdropBrush = 17;  // DWMWA_USE_HOSTBACKDROPBRUSH (Windows 11 22H2)

////////////////////////////////////////////////////////////////////////////////
// Noise texture (from the Windhawk Taskbar Styler): a tileable 256x256 grey noise BMP in memory.
// Opacity is applied by the effect graph.

wf::IInspectable CreateNoiseSurface(float density) {
    constexpr int kSize = 256;
    constexpr DWORD kBpp = 32;
    constexpr DWORD rowSize = kSize * (kBpp / 8);
    constexpr DWORD dataSize = rowSize * kSize;

    BITMAPFILEHEADER fileHeader{};
    fileHeader.bfType = 0x4D42;  // "BM"
    fileHeader.bfSize = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER) + dataSize;
    fileHeader.bfOffBits = sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER);

    BITMAPINFOHEADER infoHeader{};
    infoHeader.biSize = sizeof(BITMAPINFOHEADER);
    infoHeader.biWidth = kSize;
    infoHeader.biHeight = kSize;
    infoHeader.biPlanes = 1;
    infoHeader.biBitCount = kBpp;
    infoHeader.biSizeImage = dataSize;

    std::vector<uint8_t> pixels(dataSize);
    float exponent = 1.0f / std::clamp(density, 0.001f, 1.0f);
    uint8_t lut[256];
    for (int i = 0; i < 256; i++) {
        lut[i] = static_cast<uint8_t>(std::pow(i / 255.0f, exponent) * 255.0f);
    }

    std::mt19937 rng(0);
    std::uniform_int_distribution<int> dist(0, 255);
    for (size_t i = 0; i < pixels.size(); i += 4) {
        uint8_t gray = lut[dist(rng)];
        pixels[i] = gray;
        pixels[i + 1] = gray;
        pixels[i + 2] = gray;
        pixels[i + 3] = 255;
    }

    winrt::Windows::Storage::Streams::InMemoryRandomAccessStream stream;
    winrt::Windows::Storage::Streams::DataWriter writer(stream);
    writer.WriteBytes(winrt::array_view<const uint8_t>(
        reinterpret_cast<const uint8_t*>(&fileHeader), sizeof(fileHeader)));
    writer.WriteBytes(winrt::array_view<const uint8_t>(
        reinterpret_cast<const uint8_t*>(&infoHeader), sizeof(infoHeader)));
    writer.WriteBytes(pixels);
    writer.StoreAsync().get();
    writer.DetachStream();

    return winrt::Windows::UI::Xaml::Media::LoadedImageSurface::StartLoadFromStream(stream);
}

////////////////////////////////////////////////////////////////////////////////
// Settings

struct Settings {
    float blurAmount = 30.0f;
    winrt::Windows::UI::Color tint{0, 0, 0, 0};  // A = tint opacity
    float tintLuminosityOpacity = 0.0f;
    float tintSaturation = 1.0f;
    float noiseOpacity = 0.0f;
    float noiseDensity = 1.0f;
    int cornerRadius = -1;  // -1 = follow Ghostwriter's "radius"
    bool onlyWhenAppBlurIsNone = true;
    bool applyToStatusPill = true;
    bool enableBlur = true;
    bool enableCorners = true;
};

Settings ReadSettings() {
    Settings s;
    s.blurAmount = static_cast<float>(std::clamp(Wh_GetIntSetting(L"BlurAmount"), 0, 250));

    uint8_t r = 0, g = 0, b = 0;
    if (PCWSTR text = Wh_GetStringSetting(L"TintColor")) {
        std::wstring_view hex = text;
        if (!hex.empty() && hex[0] == L'#') {
            hex.remove_prefix(1);
        }
        if (hex.size() == 8) {
            hex.remove_prefix(2);  // an alpha part is ignored: the opacity has its own setting
        }
        if (hex.size() == 6) {
            wchar_t* end = nullptr;
            std::wstring copy(hex);
            unsigned long value = wcstoul(copy.c_str(), &end, 16);
            if (end && *end == L'\0') {
                r = static_cast<uint8_t>(value >> 16);
                g = static_cast<uint8_t>(value >> 8);
                b = static_cast<uint8_t>(value);
            }
        }
        Wh_FreeStringSetting(text);
    }
    int opacity = std::clamp(Wh_GetIntSetting(L"TintOpacity"), 0, 100);
    s.tint = winrt::Windows::UI::Color{static_cast<uint8_t>(std::lround(opacity * 2.55)), r, g, b};

    s.tintLuminosityOpacity = std::clamp(Wh_GetIntSetting(L"TintLuminosityOpacity"), 0, 100) / 100.0f;
    s.tintSaturation = std::clamp(Wh_GetIntSetting(L"TintSaturation"), 0, 400) / 100.0f;
    s.noiseOpacity = std::clamp(Wh_GetIntSetting(L"NoiseOpacity"), 0, 100) / 100.0f;
    s.noiseDensity = std::clamp(Wh_GetIntSetting(L"NoiseDensity"), 1, 100) / 100.0f;
    s.cornerRadius = std::clamp(Wh_GetIntSetting(L"CornerRadius"), -1, 64);
    s.onlyWhenAppBlurIsNone = Wh_GetIntSetting(L"OnlyWhenAppBlurIsNone") != 0;
    s.applyToStatusPill = Wh_GetIntSetting(L"ApplyToStatusPill") != 0;
    s.enableBlur = Wh_GetIntSetting(L"EnableBlur") != 0;
    s.enableCorners = Wh_GetIntSetting(L"EnableCorners") != 0;
    return s;
}

////////////////////////////////////////////////////////////////////////////////
// Ghostwriter's own settings: only the [appearance] values radius and blur are needed.

struct AppConfig {
    int radius = 10;
    std::wstring blur = L"acrylic";  // the app's defaults when the block is missing
};

static std::string Trim(std::string_view text) {
    size_t begin = text.find_first_not_of(" \t\r");
    if (begin == std::string_view::npos) {
        return {};
    }
    size_t end = text.find_last_not_of(" \t\r");
    return std::string(text.substr(begin, end - begin + 1));
}

static std::string StripComment(std::string_view line) {
    bool quoted = false;
    for (size_t i = 0; i < line.size(); i++) {
        if (line[i] == '"') {
            quoted = !quoted;
        } else if (line[i] == '#' && !quoted) {
            return std::string(line.substr(0, i));
        }
    }
    return std::string(line);
}

AppConfig ParseAppConfig(const std::string& text) {
    AppConfig config;
    bool inAppearance = false;
    size_t pos = 0;
    while (pos <= text.size()) {
        size_t eol = text.find('\n', pos);
        if (eol == std::string::npos) {
            eol = text.size();
        }
        std::string line = Trim(StripComment(std::string_view(text).substr(pos, eol - pos)));
        pos = eol + 1;
        if (line.empty()) {
            continue;
        }
        if (line[0] == '[') {
            std::string compact;
            for (char c : line) {
                if (c != ' ' && c != '\t') {
                    compact += c;
                }
            }
            inAppearance = compact == "[appearance]";
            continue;
        }
        if (!inAppearance) {
            continue;
        }
        size_t eq = line.find('=');
        if (eq == std::string::npos) {
            continue;
        }
        std::string key = Trim(std::string_view(line).substr(0, eq));
        std::string value = Trim(std::string_view(line).substr(eq + 1));
        if (key == "radius") {
            char* end = nullptr;
            long number = strtol(value.c_str(), &end, 10);
            if (end && *end == '\0' && number >= 0 && number <= 32) {
                config.radius = static_cast<int>(number);
            }
        } else if (key == "blur") {
            std::wstring blur;
            for (char c : value) {
                if (c != '"') {
                    blur += static_cast<wchar_t>(std::tolower(static_cast<unsigned char>(c)));
                }
            }
            config.blur = blur;
        }
    }
    return config;
}

////////////////////////////////////////////////////////////////////////////////
// The worker: one thread with its own message loop and Composition compositor.

constexpr wchar_t kHelperClass[] = L"GhostwriterBlurHelper";
constexpr UINT kMsgSettingsChanged = WM_APP + 1;

HMODULE g_module = nullptr;
HANDLE g_thread = nullptr;
DWORD g_threadId = 0;
HANDLE g_ready = nullptr;

std::mutex g_settingsMutex;
Settings g_pendingSettings;

// Everything below is only touched on the worker thread.
Settings g_cfg;
AppConfig g_app;
std::wstring g_appPath;
FILETIME g_appStamp{};
bool g_appStampValid = false;
bool g_loggedWaiting = false;
wuc::Compositor g_compositor{nullptr};
DWORD g_ownPid = 0;

struct Helper {
    HWND hwnd = nullptr;
    wuc::Desktop::DesktopWindowTarget target{nullptr};
    wuc::ContainerVisual root{nullptr};
    wuc::SpriteVisual sprite{nullptr};
    wuc::CompositionRoundedRectangleGeometry geometry{nullptr};
    int width = -1;
    int height = -1;
    float radius = -1.0f;
};
std::unordered_map<HWND, Helper> g_helpers;  // key: the Ghostwriter window

enum class Kind { None, Overlay, Status };

Kind ClassifyWindow(HWND hwnd) {
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid != g_ownPid || GetAncestor(hwnd, GA_ROOT) != hwnd) {
        return Kind::None;
    }
    wchar_t className[64]{};
    GetClassNameW(hwnd, className, ARRAYSIZE(className));
    if (wcsncmp(className, L"HwndWrapper[Ghostwriter", 22) != 0) {
        return Kind::None;
    }
    wchar_t title[64]{};
    GetWindowTextW(hwnd, title, ARRAYSIZE(title));
    if (wcscmp(title, L"Ghostwriter") == 0) {
        return Kind::Overlay;
    }
    if (wcscmp(title, L"Ghostwriter Status") == 0) {
        return Kind::Status;
    }
    return Kind::None;
}

void RefreshAppConfig() {
    if (g_appPath.empty()) {
        wchar_t dir[MAX_PATH]{};
        DWORD n = GetEnvironmentVariableW(L"GHOSTWRITER_CONFIG_DIR", dir, ARRAYSIZE(dir));
        if (n == 0 || n >= ARRAYSIZE(dir)) {
            n = GetEnvironmentVariableW(L"APPDATA", dir, ARRAYSIZE(dir));
            if (n == 0 || n >= ARRAYSIZE(dir)) {
                return;
            }
            wcscat_s(dir, L"\\Ghostwriter");
        }
        g_appPath = std::wstring(dir) + L"\\settings.toml";
    }

    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(g_appPath.c_str(), GetFileExInfoStandard, &data)) {
        return;
    }
    if (g_appStampValid && CompareFileTime(&data.ftLastWriteTime, &g_appStamp) == 0) {
        return;
    }

    HANDLE file = CreateFileW(g_appPath.c_str(), GENERIC_READ,
                              FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
                              OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        return;
    }
    std::string text;
    char buffer[8192];
    DWORD read = 0;
    while (ReadFile(file, buffer, sizeof(buffer), &read, nullptr) && read > 0) {
        text.append(buffer, read);
    }
    CloseHandle(file);

    g_app = ParseAppConfig(text);
    g_appStamp = data.ftLastWriteTime;
    g_appStampValid = true;
    Wh_Log(L"Ghostwriter settings: radius=%d, blur=%s", g_app.radius, g_app.blur.c_str());
}

float RadiusDip() {
    return static_cast<float>(g_cfg.cornerRadius >= 0 ? g_cfg.cornerRadius : g_app.radius);
}

bool AppAllowsHelper() {
    return g_cfg.enableBlur && (!g_cfg.onlyWhenAppBlurIsNone || g_app.blur == L"none");
}

float DpiScale(HWND hwnd) {
    HMONITOR monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
    UINT dpiX = 96, dpiY = 96;
    using GetDpiForMonitor_t = HRESULT(WINAPI*)(HMONITOR, int, UINT*, UINT*);
    static auto getDpiForMonitor = reinterpret_cast<GetDpiForMonitor_t>(
        GetProcAddress(LoadLibraryExW(L"shcore.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32),
                       "GetDpiForMonitor"));
    if (getDpiForMonitor) {
        getDpiForMonitor(monitor, 0 /*MDT_EFFECTIVE_DPI*/, &dpiX, &dpiY);
    }
    return dpiX / 96.0f;
}

// The effect graph of WindhawkBlur: host backdrop -> blur -> (saturation) -> (luminosity) -> (noise) -> tint.
wuc::CompositionBrush BuildBrush() {
    namespace wuxm = winrt::Windows::UI::Xaml::Media;
    constexpr float kLumaR = 0.2126f;
    constexpr float kLumaG = 0.7152f;
    constexpr float kLumaB = 0.0722f;

    auto backdropBrush = g_compositor.CreateHostBackdropBrush();

    auto blurEffect = winrt::make_self<GaussianBlurEffect>();
    blurEffect->Source = wuc::CompositionEffectSourceParameter(L"backdrop");
    blurEffect->BlurAmount = g_cfg.blurAmount;
    blurEffect->Name(L"BlurEffect");
    wge::IGraphicsEffectSource topOfStack = *blurEffect;

    if (g_cfg.tintSaturation != 1.0f) {
        float s = std::max(g_cfg.tintSaturation, 0.0f);
        float invS = 1.0f - s;
        auto satMatrix = winrt::make_self<ColorMatrixEffect>();
        satMatrix->Source = topOfStack;
        auto& m = satMatrix->Matrix;
        m[0] = invS * kLumaR + s; m[1] = invS * kLumaR;     m[2] = invS * kLumaR;     m[3] = 0.0f;
        m[4] = invS * kLumaG;     m[5] = invS * kLumaG + s; m[6] = invS * kLumaG;     m[7] = 0.0f;
        m[8] = invS * kLumaB;     m[9] = invS * kLumaB;     m[10] = invS * kLumaB + s; m[11] = 0.0f;
        m[12] = 0.0f;             m[13] = 0.0f;             m[14] = 0.0f;             m[15] = 1.0f;
        satMatrix->Name(L"SaturationEffect");
        topOfStack = *satMatrix;
    }

    if (g_cfg.tintLuminosityOpacity > 0.0f) {
        float op = g_cfg.tintLuminosityOpacity;
        float tintLum = (g_cfg.tint.R / 255.0f) * kLumaR + (g_cfg.tint.G / 255.0f) * kLumaG +
                        (g_cfg.tint.B / 255.0f) * kLumaB;
        auto lumMatrix = winrt::make_self<ColorMatrixEffect>();
        lumMatrix->Source = topOfStack;
        auto& m = lumMatrix->Matrix;
        m[0] = 1.0f - (kLumaR * op); m[1] = -(kLumaR * op);       m[2] = -(kLumaR * op);       m[3] = 0.0f;
        m[4] = -(kLumaG * op);       m[5] = 1.0f - (kLumaG * op); m[6] = -(kLumaG * op);       m[7] = 0.0f;
        m[8] = -(kLumaB * op);       m[9] = -(kLumaB * op);       m[10] = 1.0f - (kLumaB * op); m[11] = 0.0f;
        m[12] = 0.0f;                m[13] = 0.0f;                m[14] = 0.0f;                m[15] = 1.0f;
        m[16] = tintLum * op;        m[17] = tintLum * op;        m[18] = tintLum * op;        m[19] = 0.0f;
        lumMatrix->Name(L"LuminosityBlend");
        topOfStack = *lumMatrix;
    }

    wuc::CompositionSurfaceBrush noiseBrush{nullptr};
    wge::IGraphicsEffectSource stackBeforeNoise = topOfStack;
    if (g_cfg.noiseOpacity > 0.0f) {
        try {
            auto surface = CreateNoiseSurface(g_cfg.noiseDensity).as<wuc::ICompositionSurface>();
            noiseBrush = g_compositor.CreateSurfaceBrush(surface);
            noiseBrush.Stretch(wuc::CompositionStretch::None);

            auto borderEffect = winrt::make_self<BorderEffect>();
            borderEffect->Source = wuc::CompositionEffectSourceParameter(L"NoiseSource");

            auto opacityEffect = winrt::make_self<ColorMatrixEffect>();
            opacityEffect->Source = *borderEffect;
            opacityEffect->Matrix[0] = g_cfg.noiseOpacity;
            opacityEffect->Matrix[5] = g_cfg.noiseOpacity;
            opacityEffect->Matrix[10] = g_cfg.noiseOpacity;
            opacityEffect->Matrix[15] = g_cfg.noiseOpacity;
            opacityEffect->Name(L"NoiseOpacityEffect");

            auto noiseComposite = winrt::make_self<CompositeEffect>();
            noiseComposite->Mode = D2D1_COMPOSITE_MODE_SOURCE_OVER;
            noiseComposite->Sources.push_back(topOfStack);
            noiseComposite->Sources.push_back(*opacityEffect);
            noiseComposite->Name(L"NoiseComposite");
            topOfStack = *noiseComposite;
        } catch (...) {
            Wh_Log(L"Noise texture unavailable (%08X); continuing without noise",
                   static_cast<unsigned>(winrt::to_hresult()));
            noiseBrush = nullptr;
            topOfStack = stackBeforeNoise;
        }
    }

    auto floodEffect = winrt::make_self<FloodEffect>();
    floodEffect->Color = g_cfg.tint;
    floodEffect->Name(L"FloodEffect");

    auto compositeEffect = winrt::make_self<CompositeEffect>();
    compositeEffect->Mode = D2D1_COMPOSITE_MODE_SOURCE_OVER;
    compositeEffect->Sources.push_back(topOfStack);
    compositeEffect->Sources.push_back(*floodEffect);

    auto factory = g_compositor.CreateEffectFactory(*compositeEffect);
    auto brush = factory.CreateBrush();
    brush.SetSourceParameter(L"backdrop", backdropBrush);
    if (noiseBrush) {
        brush.SetSourceParameter(L"NoiseSource", noiseBrush);
    }
    return brush;
}

LRESULT CALLBACK HelperProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_NCHITTEST:
            return HTTRANSPARENT;
        case WM_MOUSEACTIVATE:
            return MA_NOACTIVATE;
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

Helper& EnsureHelper(HWND target) {
    auto found = g_helpers.find(target);
    if (found != g_helpers.end()) {
        return found->second;
    }

    Helper helper;
    // Click-through for every process (WS_EX_LAYERED + WS_EX_TRANSPARENT), no taskbar button, never active.
    helper.hwnd = CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE |
                                      WS_EX_TRANSPARENT | WS_EX_LAYERED,
                                  kHelperClass, L"Ghostwriter Blur", WS_POPUP, 0, 0, 1, 1, nullptr,
                                  nullptr, g_module, nullptr);
    if (!helper.hwnd) {
        winrt::throw_last_error();
    }

    BOOL on = TRUE;
    DwmSetWindowAttribute(helper.hwnd, kDwmaUseHostBackdropBrush, &on, sizeof(on));
    int cornerPreference = 1;  // DWMWCP_DONOTROUND: the shape is the clip below
    DwmSetWindowAttribute(helper.hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &cornerPreference,
                          sizeof(cornerPreference));
    DWORD noBorder = 0xFFFFFFFE;  // DWMWA_COLOR_NONE
    DwmSetWindowAttribute(helper.hwnd, DWMWA_BORDER_COLOR, &noBorder, sizeof(noBorder));

    DesktopInterop* interop = nullptr;
    winrt::check_hresult(winrt::get_unknown(g_compositor)
                             ->QueryInterface(kDesktopInteropIid, reinterpret_cast<void**>(&interop)));
    void* rawTarget = nullptr;
    HRESULT hr = interop->CreateDesktopWindowTarget(helper.hwnd, FALSE, &rawTarget);
    interop->Release();
    winrt::check_hresult(hr);
    helper.target = wuc::Desktop::DesktopWindowTarget{rawTarget, winrt::take_ownership_from_abi};

    helper.root = g_compositor.CreateContainerVisual();
    helper.target.Root(helper.root);

    helper.sprite = g_compositor.CreateSpriteVisual();
    helper.sprite.Brush(BuildBrush());
    helper.geometry = g_compositor.CreateRoundedRectangleGeometry();
    helper.sprite.Clip(g_compositor.CreateGeometricClip(helper.geometry));
    helper.root.Children().InsertAtTop(helper.sprite);

    return g_helpers.emplace(target, std::move(helper)).first->second;
}

void DestroyHelper(HWND target) {
    auto found = g_helpers.find(target);
    if (found == g_helpers.end()) {
        return;
    }
    Helper& helper = found->second;
    helper.sprite = nullptr;
    helper.geometry = nullptr;
    helper.root = nullptr;
    if (helper.target) {
        helper.target.Close();
        helper.target = nullptr;
    }
    if (helper.hwnd) {
        DestroyWindow(helper.hwnd);
    }
    g_helpers.erase(found);
}

void HideHelper(HWND target) {
    auto found = g_helpers.find(target);
    if (found != g_helpers.end() && IsWindowVisible(found->second.hwnd)) {
        ShowWindow(found->second.hwnd, SW_HIDE);
    }
}

// Puts the helper window directly below the Ghostwriter window, with the same bounds and a matching rounded clip.
void Sync(HWND target) {
    Kind kind = ClassifyWindow(target);
    if (kind == Kind::None || (kind == Kind::Status && !g_cfg.applyToStatusPill)) {
        HideHelper(target);
        return;
    }

    RECT rect{};
    DWORD cloaked = 0;
    DwmGetWindowAttribute(target, DWMWA_CLOAKED, &cloaked, sizeof(cloaked));
    bool visible = IsWindowVisible(target) && !IsIconic(target) && cloaked == 0 &&
                   GetWindowRect(target, &rect) && rect.left > -30000 && rect.top > -30000 &&
                   rect.right > rect.left && rect.bottom > rect.top;

    if (visible && !AppAllowsHelper()) {
        if (!g_loggedWaiting) {
            Wh_Log(L"Ghostwriter uses blur=\"%s\"; the mod waits for blur=\"none\"", g_app.blur.c_str());
            g_loggedWaiting = true;
        }
        visible = false;
    } else if (visible) {
        g_loggedWaiting = false;
    }

    if (!visible) {
        HideHelper(target);
        return;
    }

    Helper& helper = EnsureHelper(target);
    int width = rect.right - rect.left;
    int height = rect.bottom - rect.top;
    float radius = RadiusDip() * DpiScale(target);
    if (width != helper.width || height != helper.height || radius != helper.radius) {
        helper.sprite.Size({static_cast<float>(width), static_cast<float>(height)});
        helper.geometry.Size({static_cast<float>(width), static_cast<float>(height)});
        helper.geometry.CornerRadius({radius, radius});
        helper.width = width;
        helper.height = height;
        helper.radius = radius;
    }

    // The helper must be topmost exactly when the Ghostwriter window is; then it is placed right below it.
    bool targetTopmost = (GetWindowLongPtrW(target, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
    bool helperTopmost = (GetWindowLongPtrW(helper.hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
    if (targetTopmost != helperTopmost) {
        SetWindowPos(helper.hwnd, targetTopmost ? HWND_TOPMOST : HWND_NOTOPMOST, rect.left, rect.top,
                     width, height, SWP_NOACTIVATE);
    }
    SetWindowPos(helper.hwnd, target, rect.left, rect.top, width, height,
                 SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

void CALLBACK OnWinEvent(HWINEVENTHOOK, DWORD event, HWND hwnd, LONG idObject, LONG idChild, DWORD,
                         DWORD) {
    if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF || !hwnd) {
        return;
    }
    try {
        if (event == EVENT_OBJECT_DESTROY) {
            DestroyHelper(hwnd);
            return;
        }
        if (ClassifyWindow(hwnd) == Kind::None) {
            return;
        }
        if (event == EVENT_OBJECT_SHOW) {
            RefreshAppConfig();  // picks up a changed radius or blur in settings.toml
        }
        Sync(hwnd);
    } catch (...) {
        Wh_Log(L"Event %04X error: %08X", event, static_cast<unsigned>(winrt::to_hresult()));
    }
}

BOOL CALLBACK SyncExisting(HWND hwnd, LPARAM) {
    if (ClassifyWindow(hwnd) != Kind::None) {
        try {
            Sync(hwnd);
        } catch (...) {
            Wh_Log(L"Sync error: %08X", static_cast<unsigned>(winrt::to_hresult()));
        }
    }
    return TRUE;
}

void ApplyNewSettings() {
    {
        std::lock_guard lock(g_settingsMutex);
        g_cfg = g_pendingSettings;
    }
    g_loggedWaiting = false;
    for (auto& [target, helper] : g_helpers) {
        try {
            helper.sprite.Brush(BuildBrush());
            helper.radius = -1.0f;  // forces the clip to be updated
        } catch (...) {
            Wh_Log(L"Rebuilding the brush failed: %08X", static_cast<unsigned>(winrt::to_hresult()));
        }
    }
    EnumWindows(SyncExisting, 0);
}

bool ShutdownQueue(winrt::Windows::System::DispatcherQueueController const& queue) {
    using winrt::Windows::Foundation::AsyncStatus;
    try {
        auto operation = queue.ShutdownQueueAsync();
        ULONGLONG deadline = GetTickCount64() + 5000;
        while (operation.Status() == AsyncStatus::Started && GetTickCount64() < deadline) {
            MsgWaitForMultipleObjectsEx(0, nullptr, 50, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
            MSG msg;
            while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
        }
        if (operation.Status() == AsyncStatus::Started) {
            Wh_Log(L"Dispatcher queue shutdown timed out");
            return false;
        }
        operation.GetResults();
    } catch (...) {
        Wh_Log(L"Dispatcher queue shutdown error: %08X", static_cast<unsigned>(winrt::to_hresult()));
    }
    return true;
}

DWORD WINAPI WorkerMain(void*) {
    // Window bounds are physical pixels, and so is everything positioned from them.
    SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    MSG msg;
    PeekMessageW(&msg, nullptr, WM_USER, WM_USER, PM_NOREMOVE);  // creates this thread's message queue
    g_ownPid = GetCurrentProcessId();

    bool apartment = false;
    bool registered = false;
    HWINEVENTHOOK hook = nullptr;
    HMODULE coreMessaging = nullptr;
    winrt::Windows::System::DispatcherQueueController queue{nullptr};
    bool queueSafeToDrop = true;

    try {
        winrt::init_apartment(winrt::apartment_type::single_threaded);
        apartment = true;

        // Composition needs a dispatcher queue on the thread that owns the compositor.
        coreMessaging = LoadLibraryExW(L"CoreMessaging.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!coreMessaging) {
            winrt::throw_last_error();
        }
        auto createQueue = reinterpret_cast<CreateDispatcherQueueController_t>(
            GetProcAddress(coreMessaging, "CreateDispatcherQueueController"));
        if (!createQueue) {
            winrt::throw_last_error();
        }
        DispatcherQueueOptionsAbi options{sizeof(DispatcherQueueOptionsAbi), kDispatcherQueueThreadCurrent,
                                          kDispatcherQueueComSta};
        winrt::check_hresult(createQueue(options, winrt::put_abi(queue)));

        WNDCLASSEXW windowClass{};
        windowClass.cbSize = sizeof(windowClass);
        windowClass.lpfnWndProc = HelperProc;
        windowClass.hInstance = g_module;
        windowClass.lpszClassName = kHelperClass;
        if (!RegisterClassExW(&windowClass)) {
            winrt::throw_last_error();
        }
        registered = true;

        g_compositor = wuc::Compositor();
        {
            std::lock_guard lock(g_settingsMutex);
            g_cfg = g_pendingSettings;
        }
        RefreshAppConfig();

        // Only events of this process: the Ghostwriter windows.
        hook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_LOCATIONCHANGE, nullptr, OnWinEvent,
                               g_ownPid, 0, WINEVENT_OUTOFCONTEXT);
        if (!hook) {
            Wh_Log(L"SetWinEventHook failed (%lu)", GetLastError());
        }
        EnumWindows(SyncExisting, 0);
        Wh_Log(L"Worker started");
    } catch (...) {
        Wh_Log(L"Worker start failed: %08X", static_cast<unsigned>(winrt::to_hresult()));
    }

    SetEvent(g_ready);

    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        if (msg.message == kMsgSettingsChanged && !msg.hwnd) {
            try {
                ApplyNewSettings();
            } catch (...) {
                Wh_Log(L"Applying settings failed: %08X", static_cast<unsigned>(winrt::to_hresult()));
            }
            continue;
        }
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    // Shutdown, in the order that keeps Composition happy.
    if (hook) {
        UnhookWinEvent(hook);
    }
    std::vector<HWND> targets;
    for (auto const& entry : g_helpers) {
        targets.push_back(entry.first);
    }
    for (HWND target : targets) {
        DestroyHelper(target);
    }
    g_compositor = nullptr;
    if (queue) {
        queueSafeToDrop = ShutdownQueue(queue);
        if (queueSafeToDrop) {
            queue = nullptr;
        } else {
            winrt::detach_abi(queue);  // still running: leak it rather than free it from under the system
        }
    }
    if (registered) {
        UnregisterClassW(kHelperClass, g_module);
    }
    if (apartment) {
        winrt::uninit_apartment();
    }
    if (coreMessaging) {
        FreeLibrary(coreMessaging);
    }
    return 0;
}

////////////////////////////////////////////////////////////////////////////////
// The blur part (runs inside Ghostwriter.exe)

BOOL InitBlur() {
    Wh_Log(L"Init");
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCWSTR>(&InitBlur), &g_module);

    {
        std::lock_guard lock(g_settingsMutex);
        g_pendingSettings = ReadSettings();
    }

    g_ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    g_thread = CreateThread(nullptr, 0, WorkerMain, nullptr, 0, &g_threadId);
    if (!g_thread || !g_ready) {
        Wh_Log(L"Could not start the worker thread");
        return FALSE;
    }
    WaitForSingleObject(g_ready, 5000);
    return TRUE;
}

void BlurSettingsChanged() {
    {
        std::lock_guard lock(g_settingsMutex);
        g_pendingSettings = ReadSettings();
    }
    if (g_thread) {
        PostThreadMessageW(g_threadId, kMsgSettingsChanged, 0, 0);
    }
}

void UninitBlur() {
    Wh_Log(L"Uninit");
    if (g_thread) {
        PostThreadMessageW(g_threadId, WM_QUIT, 0, 0);
        if (WaitForSingleObject(g_thread, 10000) == WAIT_TIMEOUT) {
            Wh_Log(L"Worker did not stop in time");
        }
        CloseHandle(g_thread);
        g_thread = nullptr;
    }
    if (g_ready) {
        CloseHandle(g_ready);
        g_ready = nullptr;
    }
}

////////////////////////////////////////////////////////////////////////////////
// The corner part (runs inside dwm.exe)
//
// DWM cuts a window and its system material to a corner radius it takes from the window's "corner style": 0, 4 or 8 px.
// The radius is computed in udwm.dll by CTopLevelWindow::GetRadiusFromCornerStyle (and, on newer builds, once more by
// GetFloatCornerRadiusForCurrentStyle). Both are hooked here; for a Ghostwriter window they return the radius Ghostwriter
// published, for every other window they return what DWM computed.
//
// The approach and the symbol names follow "Custom Window Corner Radius" by m417z (GPL-3.0).

namespace dwmside {

// Ghostwriter sets this property on its windows: the radius in px at 100% scaling, plus one (so that 0 means "not set").
constexpr wchar_t kRadiusProp[] = L"Ghostwriter.CornerRadius";

std::atomic<bool> g_enabled{true};
std::atomic<int> g_override{-1};  // -1 = follow the radius Ghostwriter published

using GetWindowData_t = void*(WINAPI*)(void* self);
GetWindowData_t GetWindowData_Original = nullptr;
void* IsGhostWindow_Func = nullptr;

// Where CWindowData keeps its HWND. Not exported, so it is read from the code of CWindowData::IsGhostWindow, which loads
// the HWND member first: the first "mov reg, [rcx+0x..]" in its first instructions.
size_t g_hwndOffset = SIZE_MAX;

size_t FindHwndOffset(void* function) {
    BYTE* p = static_cast<BYTE*>(function);
    for (int i = 0; i < 12; i++) {
        WH_DISASM_RESULT result;
        if (!Wh_Disasm(p, &result)) {
            break;
        }
        p += result.length;

        const char* text = result.text;
        if (strcmp(text, "ret") == 0) {
            break;
        }
        if (strncmp(text, "mov ", 4) != 0) {
            continue;
        }
        const char* bracket = strstr(text, "[rcx+0x");
        if (!bracket) {
            continue;
        }
        char* end = nullptr;
        unsigned long long value = strtoull(bracket + 7, &end, 16);
        if (end && *end == ']') {
            return static_cast<size_t>(value);
        }
    }
    return SIZE_MAX;
}

HWND HwndOf(void* topLevelWindow) {
    if (g_hwndOffset == SIZE_MAX || !GetWindowData_Original) {
        return nullptr;
    }
    void* data = GetWindowData_Original(topLevelWindow);
    if (!data) {
        return nullptr;
    }
    HWND hwnd = *reinterpret_cast<HWND*>(static_cast<BYTE*>(data) + g_hwndOffset);
    return IsWindow(hwnd) ? hwnd : nullptr;
}

// Returns the radius to use for the window, or -1 if it is not a Ghostwriter window (or has no radius to publish).
// Only calls that never send a message to the window: DWM must not wait for an application.
int RadiusFor(void* topLevelWindow) {
    HWND hwnd = HwndOf(topLevelWindow);
    if (!hwnd) {
        return -1;
    }
    wchar_t className[32]{};
    if (GetClassNameW(hwnd, className, ARRAYSIZE(className)) <= 0 || wcsncmp(className, L"HwndWrapper[Ghostwriter", 22) != 0) {
        return -1;
    }
    auto published = static_cast<int>(reinterpret_cast<intptr_t>(GetPropW(hwnd, kRadiusProp)));
    if (published <= 0) {
        return -1;
    }
    int overridden = g_override.load();
    return overridden >= 0 ? overridden : published - 1;
}

float Adjust(void* topLevelWindow, float original) {
    // A zero radius means "square" (maximized, snapped, or the app asked for no rounding): leave it.
    if (original <= 0.0f || !g_enabled.load()) {
        return original;
    }
    int radius = RadiusFor(topLevelWindow);
    return radius > 0 ? static_cast<float>(radius) : original;
}

using GetRadiusFromCornerStyle_t = float(WINAPI*)(void* self);
GetRadiusFromCornerStyle_t GetRadiusFromCornerStyle_Original = nullptr;
float WINAPI GetRadiusFromCornerStyle_Hook(void* self) {
    return Adjust(self, GetRadiusFromCornerStyle_Original(self));
}

using GetFloatCornerRadiusForCurrentStyle_t = float(WINAPI*)(void* self);
GetFloatCornerRadiusForCurrentStyle_t GetFloatCornerRadiusForCurrentStyle_Original = nullptr;
float WINAPI GetFloatCornerRadiusForCurrentStyle_Hook(void* self) {
    return Adjust(self, GetFloatCornerRadiusForCurrentStyle_Original(self));
}

void LoadSettings() {
    g_enabled = Wh_GetIntSetting(L"EnableCorners") != 0;
    g_override = std::clamp(Wh_GetIntSetting(L"CornerRadius"), -1, 200);
}

// A faulty hook in the window manager would take the whole desktop down with it. If DWM was restarted twice within the last
// minute (Dwminit warnings in the Application log), the mod does not load, so a crash loop cannot be made worse.
bool DwmRestartedRecently() {
    EVT_HANDLE query = EvtQuery(nullptr, L"Application",
                                L"*[System[Provider[@Name='Dwminit'] and (Level=3) and TimeCreated[timediff(@SystemTime) <= 60000]]]",
                                EvtQueryChannelPath);
    if (!query) {
        return false;
    }
    EVT_HANDLE events[2]{};
    DWORD returned = 0;
    BOOL ok = EvtNext(query, ARRAYSIZE(events), events, 1000, 0, &returned);
    for (DWORD i = 0; i < returned; i++) {
        EvtClose(events[i]);
    }
    EvtClose(query);
    return ok && returned >= ARRAYSIZE(events);
}

BOOL Init() {
    if (DwmRestartedRecently()) {
        Wh_Log(L"Not loading: the window manager restarted twice within the last minute");
        return FALSE;
    }

    LoadSettings();

    HMODULE udwm = GetModuleHandleW(L"udwm.dll");
    if (!udwm) {
        Wh_Log(L"udwm.dll is not loaded");
        return FALSE;
    }

    WindhawkUtils::SYMBOL_HOOK hooks[] = {
        // Capture only (no hook): CTopLevelWindow -> CWindowData, to find the HWND of the window being composed.
        {
            {LR"(public: class CWindowData * __cdecl CTopLevelWindow::GetWindowData(void)const )"},
            &GetWindowData_Original,
            nullptr,
            true,
        },
        // Capture only: its code shows where CWindowData keeps the HWND.
        {
            {LR"(public: bool __cdecl CWindowData::IsGhostWindow(struct HWND__ * *)const )"},
            &IsGhostWindow_Func,
            nullptr,
            true,
        },
        {
            {LR"(private: float __cdecl CTopLevelWindow::GetRadiusFromCornerStyle(void))"},
            &GetRadiusFromCornerStyle_Original,
            GetRadiusFromCornerStyle_Hook,
        },
        // Newer builds compute the radius for the window visuals here (and call the function above); older builds do not have it.
        {
            {
                LR"(public: float __cdecl CTopLevelWindow::GetFloatCornerRadiusForCurrentStyle(void))",
                LR"(private: float __cdecl CTopLevelWindow::GetFloatCornerRadiusForCurrentStyle(void))",
            },
            &GetFloatCornerRadiusForCurrentStyle_Original,
            GetFloatCornerRadiusForCurrentStyle_Hook,
            true,
        },
    };

    if (!WindhawkUtils::HookSymbols(udwm, hooks, ARRAYSIZE(hooks))) {
        Wh_Log(L"The symbols of udwm.dll were not found: no rounded corners on this Windows build");
        return FALSE;
    }

    // The hooks only take effect after this function returns, so IsGhostWindow still has its original code here.
    if (IsGhostWindow_Func) {
        g_hwndOffset = FindHwndOffset(IsGhostWindow_Func);
    }
    Wh_Log(L"HWND offset in CWindowData: 0x%zx", g_hwndOffset);
    if (g_hwndOffset == SIZE_MAX || !GetWindowData_Original) {
        // Without the HWND the mod cannot tell Ghostwriter's windows from the others, so it leaves all windows alone.
        Wh_Log(L"Cannot identify windows on this build: corners stay as Windows draws them");
    }
    return TRUE;
}

}  // namespace dwmside

////////////////////////////////////////////////////////////////////////////////
// Windhawk entry points: one file, two processes.

bool g_inDwm = false;

static bool IsDwmProcess() {
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
    const wchar_t* name = wcsrchr(path, L'\\');
    return _wcsicmp(name ? name + 1 : path, L"dwm.exe") == 0;
}

BOOL Wh_ModInit() {
    g_inDwm = IsDwmProcess();
    Wh_Log(L"Init in %s", g_inDwm ? L"dwm.exe (corners)" : L"Ghostwriter.exe (blur)");
    return g_inDwm ? dwmside::Init() : InitBlur();
}

void Wh_ModSettingsChanged() {
    if (g_inDwm) {
        dwmside::LoadSettings();
    } else {
        BlurSettingsChanged();
    }
}

void Wh_ModUninit() {
    if (!g_inDwm) {
        UninitBlur();
    }
}
