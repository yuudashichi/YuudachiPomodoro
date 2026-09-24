#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) return 1;
    std::wstring root(buffer.data(), length);
    root.resize(root.find_last_of(L"\\/"));
    const auto directory = root + L"\\app";
    const auto executable = directory + L"\\惜立番茄钟.exe";
    auto command = L"\"" + executable + L"\"";
    STARTUPINFOW startup{ sizeof(startup) };
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE,
                        0, nullptr, directory.c_str(), &startup, &process))
    {
        const auto message = L"无法启动惜立番茄钟，请确认已完整解压压缩包，且 app 文件夹与启动程序在同一目录。\n\n错误代码：" + std::to_wstring(GetLastError());
        MessageBoxW(nullptr, message.c_str(), L"惜立番茄钟", MB_OK | MB_ICONERROR);
        return 1;
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return 0;
}
