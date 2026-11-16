#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shellapi.h>
#include <string>
#include <cwctype>
#include <cstdint>

namespace {
constexpr UINT kTrayMessage = WM_APP + 120;
constexpr UINT kOpenWindowMessage = WM_APP + 121;
constexpr UINT kExitWindowMessage = WM_APP + 122;
constexpr UINT kOpenCommand = 1;
constexpr UINT kExitCommand = 2;
constexpr int kProductIcon = 101;

HWND hostWindow = nullptr;
HANDLE uiProcess = nullptr;
DWORD uiProcessId = 0;
bool exiting = false;
NOTIFYICONDATAW tray = {};
HFONT menuFont = nullptr;
std::wstring managedProgram;
UINT taskbarCreated = 0;

struct WindowSearch {
    DWORD processId;
    HWND result;
};

BOOL CALLBACK FindToolWindow(HWND window, LPARAM parameter) {
    auto* search = reinterpret_cast<WindowSearch*>(parameter);
    DWORD processId = 0;
    GetWindowThreadProcessId(window, &processId);
    if (processId != search->processId) return TRUE;
    if (GetPropW(window, L"ChatSorterMainWindow")) {
        search->result = window;
        return FALSE;
    }
    return TRUE;
}

HWND ToolWindow() {
    WindowSearch search{uiProcessId, nullptr};
    EnumWindows(FindToolWindow, reinterpret_cast<LPARAM>(&search));
    return search.result;
}

void ReleaseFinishedUi() {
    if (uiProcess) CloseHandle(uiProcess);
    uiProcess = nullptr;
    uiProcessId = 0;
}

void OpenTool() {
    if (exiting) return;
    if (uiProcess && WaitForSingleObject(uiProcess, 0) == WAIT_OBJECT_0) ReleaseFinishedUi();
    if (uiProcess) {
        if (HWND window = ToolWindow()) PostMessageW(window, kOpenWindowMessage, 0, 0);
        return;
    }
    std::wstring command = L"\"" + managedProgram + L"\" --ui";
    STARTUPINFOW startup = {};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process = {};
    if (!CreateProcessW(managedProgram.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process)) {
        MessageBoxW(hostWindow, L"界面组件启动失败，请通过安装包更新工具。", L"聊天排序工具", MB_OK | MB_ICONINFORMATION);
        return;
    }
    CloseHandle(process.hThread);
    uiProcess = process.hProcess;
    uiProcessId = process.dwProcessId;
}

void BeginExit() {
    exiting = true;
    if (uiProcess && WaitForSingleObject(uiProcess, 0) == WAIT_OBJECT_0) ReleaseFinishedUi();
    if (!uiProcess) {
        DestroyWindow(hostWindow);
        return;
    }
    if (HWND window = ToolWindow()) PostMessageW(window, kExitWindowMessage, 0, 0);
    else TerminateProcess(uiProcess, 0);
    wcscpy_s(tray.szTip, L"聊天排序工具 · 当前任务完成后退出");
    Shell_NotifyIconW(NIM_MODIFY, &tray);
}

void AddTrayIcon() {
    bool added = Shell_NotifyIconW(NIM_ADD, &tray) != FALSE;
    SetPropW(hostWindow, L"ChatSorterTrayAdded", reinterpret_cast<HANDLE>(static_cast<INT_PTR>(added ? 1 : 0)));
    tray.uVersion = NOTIFYICON_VERSION_4;
    Shell_NotifyIconW(NIM_SETVERSION, &tray);
}

void ShowTrayMenu() {
    POINT point = {};
    GetCursorPos(&point);
    HMENU menu = CreatePopupMenu();
    AppendMenuW(menu, MF_OWNERDRAW | (exiting ? MF_GRAYED : 0), kOpenCommand, L"打开聊天排序");
    AppendMenuW(menu, MF_OWNERDRAW, kExitCommand, L"退出程序");
    SetForegroundWindow(hostWindow);
    UINT command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.x, point.y, 0, hostWindow, nullptr);
    DestroyMenu(menu);
    PostMessageW(hostWindow, WM_NULL, 0, 0);
    if (command == kOpenCommand) OpenTool();
    if (command == kExitCommand) BeginExit();
}

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    if (message == taskbarCreated) { AddTrayIcon(); return 0; }
    switch (message) {
    case kTrayMessage:
        if (LOWORD(lparam) == NIN_SELECT || LOWORD(lparam) == WM_LBUTTONDBLCLK) OpenTool();
        if (LOWORD(lparam) == WM_CONTEXTMENU || LOWORD(lparam) == WM_RBUTTONUP) ShowTrayMenu();
        return 0;
    case WM_MEASUREITEM: {
        auto* item = reinterpret_cast<MEASUREITEMSTRUCT*>(lparam);
        UINT dpi = GetDpiForWindow(window);
        item->itemWidth = MulDiv(200, dpi, 96);
        item->itemHeight = MulDiv(34, dpi, 96);
        return TRUE;
    }
    case WM_DRAWITEM: {
        auto* item = reinterpret_cast<DRAWITEMSTRUCT*>(lparam);
        bool selected = (item->itemState & ODS_SELECTED) != 0;
        FillRect(item->hDC, &item->rcItem, GetSysColorBrush(selected ? COLOR_HIGHLIGHT : COLOR_MENU));
        SetBkMode(item->hDC, TRANSPARENT);
        SetTextColor(item->hDC, GetSysColor(selected ? COLOR_HIGHLIGHTTEXT : COLOR_MENUTEXT));
        HGDIOBJ previous = SelectObject(item->hDC, menuFont);
        RECT bounds = item->rcItem;
        bounds.left += MulDiv(18, GetDpiForWindow(window), 96);
        const wchar_t* text = item->itemID == kOpenCommand ? L"打开聊天排序" : L"退出程序";
        DrawTextW(item->hDC, text, -1, &bounds, DT_SINGLELINE | DT_VCENTER);
        SelectObject(item->hDC, previous);
        return TRUE;
    }
    case WM_CLOSE:
        BeginExit();
        return 0;
    case WM_QUERYENDSESSION:
        return TRUE;
    case WM_ENDSESSION:
        if (wparam) DestroyWindow(window);
        return 0;
    case WM_DESTROY:
        Shell_NotifyIconW(NIM_DELETE, &tray);
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(window, message, wparam, lparam);
}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    wchar_t module[32768] = {};
    DWORD length = GetModuleFileNameW(nullptr, module, 32768);
    std::wstring directory(module, length);
    directory.resize(directory.find_last_of(L"\\/"));
    managedProgram = directory + L"\\聊天排序工具.exe";
    int argumentCount = 0;
    LPWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
    std::wstring argument = argumentCount > 1 ? arguments[1] : L"";
    LocalFree(arguments);
    BOOL inheritedJob = FALSE;
    if (IsProcessInJob(GetCurrentProcess(), nullptr, &inheritedJob) && inheritedJob) {
        std::wstring command = L"\"" + managedProgram + L"\"";
        if (argument == L"--background" || argument == L"--exit") command += L" " + argument;
        STARTUPINFOW startup = {};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process = {};
        if (CreateProcessW(managedProgram.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process)) {
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }
        return 0;
    }

    std::uint64_t hash = 14695981039346656037ull;
    for (wchar_t character : directory) { hash ^= std::towlower(character); hash *= 1099511628211ull; }
    wchar_t suffix[17] = {};
    swprintf_s(suffix, L"%016llx", static_cast<unsigned long long>(hash));
    std::wstring prefix = L"Local\\ChatSorter-" + std::wstring(suffix);
    HANDLE openEvent = CreateEventW(nullptr, FALSE, FALSE, (prefix + L"-Open").c_str());
    HANDLE exitEvent = CreateEventW(nullptr, FALSE, FALSE, (prefix + L"-Exit").c_str());
    HANDLE singleton = CreateMutexW(nullptr, TRUE, (prefix + L"-Instance").c_str());
    bool existing = GetLastError() == ERROR_ALREADY_EXISTS;
    if (!openEvent || !exitEvent || !singleton) return 1;
    if (existing) {
        if (argument == L"--exit") SetEvent(exitEvent);
        else if (argument != L"--background") SetEvent(openEvent);
        CloseHandle(singleton);
        CloseHandle(exitEvent);
        CloseHandle(openEvent);
        return 0;
    }
    if (argument == L"--exit") {
        ReleaseMutex(singleton);
        CloseHandle(singleton);
        CloseHandle(exitEvent);
        CloseHandle(openEvent);
        return 0;
    }

    taskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");
    WNDCLASSEXW type = {};
    type.cbSize = sizeof(type);
    type.hInstance = instance;
    type.lpfnWndProc = WindowProcedure;
    type.lpszClassName = L"ChatSorterTrayHost";
    type.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(kProductIcon));
    if (!RegisterClassExW(&type)) return 1;
    hostWindow = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, type.lpszClassName,
        L"聊天排序后台", 0, 0, 0, 0, 0, nullptr, nullptr, instance, nullptr);
    if (!hostWindow) return 1;
    menuFont = CreateFontW(-MulDiv(12, GetDpiForWindow(hostWindow), 72), 0, 0, 0, FW_NORMAL,
        FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH, L"SimSun");
    tray.cbSize = sizeof(tray);
    tray.hWnd = hostWindow;
    tray.uID = 1;
    tray.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    tray.uCallbackMessage = kTrayMessage;
    tray.hIcon = type.hIcon;
    wcscpy_s(tray.szTip, L"聊天排序工具 · 双击打开，右键退出");
    AddTrayIcon();
    if (argument != L"--background") SetEvent(openEvent);

    bool running = true;
    while (running) {
        HANDLE handles[3] = {openEvent, exitEvent, uiProcess};
        DWORD count = uiProcess ? 3 : 2;
        DWORD result = MsgWaitForMultipleObjects(count, handles, FALSE, INFINITE, QS_ALLINPUT);
        if (result == WAIT_OBJECT_0) OpenTool();
        else if (result == WAIT_OBJECT_0 + 1) BeginExit();
        else if (uiProcess && result == WAIT_OBJECT_0 + 2) {
            ReleaseFinishedUi();
            if (exiting) DestroyWindow(hostWindow);
        }
        else if (result == WAIT_FAILED) running = false;
        MSG message = {};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            if (message.message == WM_QUIT) { running = false; break; }
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    ReleaseFinishedUi();
    if (menuFont) DeleteObject(menuFont);
    ReleaseMutex(singleton);
    CloseHandle(singleton);
    CloseHandle(exitEvent);
    CloseHandle(openEvent);
    return 0;
}
