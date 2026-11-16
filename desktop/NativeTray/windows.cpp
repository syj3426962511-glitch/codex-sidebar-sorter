#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <atomic>
#include <mutex>
#include <string>

static HWND window_handle;
static NOTIFYICONDATAW notification{};
static void (__cdecl *on_command)(int);
static std::atomic<bool> stopped{false};
static std::mutex captions_mutex;
static std::wstring title_text, open_text, exit_text;
static constexpr UINT tray_message = WM_APP + 1;
static constexpr UINT captions_message = WM_APP + 2;

static std::wstring wide(const char* text) {
    int count = MultiByteToWideChar(CP_UTF8, 0, text, -1, nullptr, 0);
    std::wstring result(count, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text, -1, result.data(), count);
    result.resize(count - 1);
    return result;
}

static LRESULT CALLBACK dispatch(HWND window, UINT message, WPARAM argument, LPARAM details) {
    if (message == tray_message) {
        if (LOWORD(details) == WM_LBUTTONUP) on_command(1);
        if (LOWORD(details) == WM_RBUTTONUP || LOWORD(details) == WM_CONTEXTMENU) {
            HMENU menu = CreatePopupMenu();
            {
                std::lock_guard<std::mutex> guard(captions_mutex);
                AppendMenuW(menu, MF_STRING, 1, open_text.c_str());
                AppendMenuW(menu, MF_STRING, 2, exit_text.c_str());
            }
            POINT cursor;
            GetCursorPos(&cursor);
            SetForegroundWindow(window);
            int selected = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, cursor.x, cursor.y, 0, window, nullptr);
            DestroyMenu(menu);
            if (selected) on_command(selected);
        }
        return 0;
    }
    if (message == captions_message) {
        std::lock_guard<std::mutex> guard(captions_mutex);
        wcsncpy_s(notification.szTip, title_text.c_str(), _TRUNCATE);
        Shell_NotifyIconW(NIM_MODIFY, &notification);
        return 0;
    }
    if (message == WM_CLOSE) {
        Shell_NotifyIconW(NIM_DELETE, &notification);
        DestroyWindow(window);
        return 0;
    }
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, argument, details);
}

extern "C" __declspec(dllexport) void sorter_tray_set_captions(const char* title, const char* open, const char* exit) {
    {
        std::lock_guard<std::mutex> guard(captions_mutex);
        title_text = wide(title);
        open_text = wide(open);
        exit_text = wide(exit);
    }
    if (window_handle) PostMessageW(window_handle, captions_message, 0, 0);
}

extern "C" __declspec(dllexport) void sorter_tray_stop() {
    stopped = true;
    if (window_handle) PostMessageW(window_handle, WM_CLOSE, 0, 0);
}

extern "C" __declspec(dllexport) int sorter_tray_run(const char* icon, const char* title, const char* open, const char* exit, void (__cdecl *command)(int)) {
    on_command = command;
    sorter_tray_set_captions(title, open, exit);
    HINSTANCE module = GetModuleHandleW(nullptr);
    WNDCLASSW type{};
    type.lpfnWndProc = dispatch;
    type.hInstance = module;
    type.lpszClassName = L"IndependentSidebarSorterTray";
    if (!RegisterClassW(&type)) return 1;
    window_handle = CreateWindowW(type.lpszClassName, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, module, nullptr);
    if (!window_handle) return 1;
    notification.cbSize = sizeof(notification);
    notification.hWnd = window_handle;
    notification.uID = 1;
    notification.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    notification.uCallbackMessage = tray_message;
    notification.hIcon = (HICON)LoadImageW(nullptr, wide(icon).c_str(), IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
    if (!notification.hIcon) notification.hIcon = LoadIconW(nullptr, IDI_APPLICATION);
    {
        std::lock_guard<std::mutex> guard(captions_mutex);
        wcsncpy_s(notification.szTip, title_text.c_str(), _TRUNCATE);
    }
    if (!Shell_NotifyIconW(NIM_ADD, &notification)) { DestroyWindow(window_handle); return 1; }
    if (stopped) PostMessageW(window_handle, WM_CLOSE, 0, 0);
    MSG message;
    while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    window_handle = nullptr;
    if (notification.hIcon) DestroyIcon(notification.hIcon);
    return 0;
}
