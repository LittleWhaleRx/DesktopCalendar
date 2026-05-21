# DesktopCalendar

一个 Windows 桌面半透明台历，用来记录工作计划、当天完成事项和纪念日。

## 功能

- 月历视图，支持切换月份和回到今天
- 按日期保存工作计划和当天完成记录
- 按月日重复显示纪念日
- 半透明无边框窗口，可拖拽和锁定位置
- 支持调节透明度
- 可尝试嵌入 Windows 桌面 WorkerW 层
- 本地 JSON 自动保存数据

## 运行

需要 Windows 和 .NET Desktop SDK。

```powershell
dotnet run --project .\DesktopCalendar.csproj
```

## 数据位置

运行时数据保存在：

```text
%LOCALAPPDATA%\DesktopCalendar\calendar-data.json
```

该文件不在仓库中，便于不同电脑分别保存自己的本地记录。
