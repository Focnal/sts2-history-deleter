# 杀戮尖塔 2 · 历史记录删除器（HistoryDeleter）

给《杀戮尖塔 2》的**游戏历史记录页面**（图鉴 → 历史记录）加一个删除按钮，用来删掉当前正在查看的那一局记录。

- 游戏版本：`v0.113.1`（`public-beta`）
- 依赖：[STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) `0.6.15+`
- 许可证：MIT

## 功能

- **和原版同款按钮**：直接复用存档编辑界面的「删除存档」按钮（红色垃圾桶图标 + HSV 着色器），不是仿制。
- **原版悬浮动画**：鼠标悬浮时图标放大、变亮，并飘出红字「删除此局记录」。
- **原生确认弹窗**：点击后弹出和主菜单「退出游戏」同一套确认窗；点「取消」不做任何事。
- **同时清云端**：删除当前这一局的 `.run` 与 `.run.backup`，并通过游戏自己的 `ISaveStore` 一起清掉 Steam 云副本，避免重进游戏后记录"复活"。
- **界面状态同步**：删除后自动切到相邻一局；删到最后一条时自动退出该页面并隐藏图鉴入口。

## 安装（玩家）

1. 安装 [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib/releases)（Steam 工坊或 releases 均可）。
2. 把 `HistoryDeleter.dll` 和 `HistoryDeleter.json` 放进：

   ```
   <游戏目录>\mods\HistoryDeleter\
   ```

3. 启动游戏。首次会提示已启用模组，重启后生效。

> 只在**打了 mod 的存档作用域**（`modded/profileN`）里工作，不会碰未打 mod 的 `profileN`。

## 构建（开发者）

前置：.NET SDK 9+、游戏本体、NuGet 源可访问（用于还原 `STS2.RitsuLib`）。

```powershell
./build.ps1
```

或者直接用 MSBuild。`Sts2Dir` 的解析优先级：

1. `HistoryDeleter/Local.props`（本机覆盖，已在 `.gitignore` 中忽略）
2. `-p:Sts2Dir="<游戏目录>"`
3. 环境变量 `STS2_DIR`
4. Steam 默认路径 `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`

最省事的做法：

```powershell
cd HistoryDeleter
Copy-Item Local.props.example Local.props
# 编辑 Local.props，把 Sts2Dir 改成你的游戏目录
cd ..
./build.ps1
```

其它开关：

```powershell
./build.ps1 -Sts2Dir "D:\Steam\steamapps\common\Slay the Spire 2"
./build.ps1 -SkipDeploy        # 只编译，不写游戏目录（CI 用）
```

编译产物会自动复制到 `<游戏目录>\mods\HistoryDeleter\`。

> mod 目录里的 DLL 文件名必须等于 manifest 的 `id`（游戏按 `<id>.dll` 加载），所以程序集名固定为 `HistoryDeleter`。

## 工作原理

### 1. 历史记录存在哪

```
<user://>/steam/<SteamID>/{modded/}profileN/saves/history/<StartTime>.run
                                                       <StartTime>.run.backup
```

Windows 上 `user://` 即 `%APPDATA%\SlayTheSpire2`。

- 文件名就是这局的 `StartTime`（Unix 秒），不是随机 ID。
- `.run.backup` 是**本地专属**的：`CloudSaveStore` 写目录时会过滤掉 `.backup`，永远不上传云端。备份是可选的，删除时必须两个都处理（不存在就跳过）。
- 打 mod 时用 `modded/profileN`，不打 mod 用 `profileN`，由 `UserDataPathProvider.IsRunningModded` 决定。

### 2. 为什么不能直接 `File.Delete`

`SaveManager._saveStore` 在 Steam 环境下是 `CloudSaveStore`（本地 `GodotFileIo` + `SteamRemoteSaveStore` 双层）。只删本地文件的话，下次启动的 `SyncCloudToLocalDirectory(history)` 会把云端那份同步回来，记录会"复活"。

所以删除统一走 `ISaveStore.DeleteFile(path)`：`CloudSaveStore` 会先删本地，再调 `SteamRemoteStorage.FileDelete()` 删云端。

### 3. 界面钩子

- 页面类是 `MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen.NRunHistory`。
- 它用私有 `List<string> _runNames` + `int _index` 维护游标，`RefreshAndSelectRun(int)` 刷新详情。
- mod 用 RitsuLib 的补丁框架（`IPatchMethod`）挂两个 Postfix：
  - `NRunHistory._Ready` → 添加按钮；
  - `NRunHistory.OnSubmenuOpened` → 重置按钮状态（实例会被复用）。
- 通过反射读写上述私有成员，删除文件后同步内存列表与游标。

### 4. 按钮视觉与悬浮动画

按钮不是仿制，而是**直接实例化游戏自己的场景**：

```
res://scenes/screens/profiles/profile_delete_button.tscn
└─ DeleteButton (Control 80×80, script NDeleteProfileButton)
   ├─ Icon (TextureRect, res://images/packed/main_menu/delete_button.png, HSV ShaderMaterial)
   └─ MegaLabel (420×52, 红色 24px, 带阴影, 默认 alpha=0)
```

悬浮动画也直接调用它自己的 `OnFocus()` / `OnUnfocus()`（反射），所以 `scale 1→1.1`、着色器 `v 1→1.4`、文字 `alpha 0→1` + `position.y 48→78` 等参数与原版天然一致。

**关键安全点**：`NDeleteProfileButton.OnRelease()` 会删**存档档位**。实例化后把它设为 `MouseFilter=Ignore` + `FocusMode=None`，它永远收不到点击；上面再叠一个透明 `Button` 负责悬停/点击，点击走本 mod 的确认弹窗与删除逻辑。这样既拿到原版外观和动画，又不可能误删存档。

两处适配：

- 场景里的文字本来 420px 宽、居中，贴到屏幕右边缘会溢出，所以改成**右对齐到图标右边缘**（垂直飘出距离不变）。
- 图标与文字的 `MouseFilter` 都设为 `Ignore`，避免那块 420px 宽的区域挡住下面分享按钮的点击。

### 5. 确认弹窗与本地化

弹窗就是原版「退出游戏」那套：

```csharp
var popup = NGenericPopup.Create();
NModalContainer.Instance.Add(popup);
if (await popup.WaitForConfirmation(body, header, cancel, confirm)) { ... }
```

难点是 `WaitForConfirmation` 只收 `LocString`，而 `LocString` 只是「(表名, 键名)」的查询句柄——文本必须已在某张本地化表里，纯代码 mod 又没有 `.pck` 可以放表。

做法：弹窗时用游戏公开 API 往已加载的 `main_menu_ui` 表里合并两个带命名空间的键

```csharp
LocManager.Instance.GetTable("main_menu_ui").MergeWith(new Dictionary<string, string> {
    ["HISTORY_DELETER.CONFIRM.header"] = "删除此局记录？",
    ["HISTORY_DELETER.CONFIRM.body"]   = "将永久删除当前查看的这一局记录，包括它的 Steam 云端副本。\n此操作无法撤销。",
});
```

按钮文字复用原版 `GENERIC_POPUP.cancel` / `GENERIC_POPUP.confirm`，任何语言下都正确；标题/正文按当前语言（zh / en）生成，每次弹窗重新合并，切换语言后依然有效。

### 6. 删除后的界面状态

- 从 `_runNames` 移除该条，游标移到 `min(index, count-1)`，再调 `RefreshAndSelectRun` 刷新详情与左右箭头。
- 如果删的是最后一条：`NSubmenuStack.Pop()` 退出页面，并手动把图鉴里的「历史记录」入口设为 `NRunHistory.CanBeShown()` 的结果——本体只在 `_Ready` 里设置过一次可见性，不补这一步下次点进去会抛 `Tried to show run history screen with no runs!`。

## 按钮位置

以屏幕右下角为锚点，与 `ShareButton` **右边缘对齐、位于其正上方**：

| 控件 | anchors | offsets |
| --- | --- | --- |
| `ShareButton`（原版） | (1,1) | `-220,-112` ~ `-48,-48` |
| 本 mod 删除按钮 | (1,1) | `-128,-252` ~ `-48,-172` |

两者之间留 **60px**，正好是飘出文字所需的高度（1.1 倍缩放下约 139.5px），文字只落在空隙里，不会盖住分享按钮。

需要微调时改 `src/Ui/DeleteRunButton.cs` 顶部这 4 个常量，重新构建即可：

```csharp
private const float OffsetLeft  = -128f;
private const float OffsetTop   = -252f;
private const float OffsetRight = -48f;
private const float OffsetBottom= -172f;
```

## 测试清单

启动游戏 → 主菜单 → 图鉴（Compendium）→ 历史记录。

1. **按钮出现**：分享按钮正上方、右边缘对齐，红色垃圾桶图标。
2. **悬浮飘字**：移到图标上 → 放大变亮 + 下方飘出「删除此局记录」；移开淡出。
3. **确认弹窗**：点击弹出原生确认窗；点「取消」不删任何东西（这套弹窗点窗外不会关闭，属正常）。
4. **真删除**：点「确认」后页面切到相邻一局；`...\saves\history\` 里对应的 `<StartTime>.run` 与 `.run.backup` 消失。
5. **云端**：开着 Steam 云时，退出重进，被删的那条不应回来。
6. **删到最后一条**：历史记录页自动关闭，图鉴入口消失。
7. **日志**：`%APPDATA%\SlayTheSpire2\logs\godot.log` 里搜 `HistoryDeleter`：

```
[INFO] [HistoryDeleter] initializing
[INFO] [HistoryDeleter] delete button added (rect=..., label=vanilla hover animation).
[INFO] [HistoryDeleter] deletion cancelled.                        # 点了「取消」
[INFO] [HistoryDeleter] removed 2 file(s) for 'modded/profile1/saves/history/1790xxxxxx.run'
```

如果出现 `label=fallback`，说明没找到原版 `OnFocus`/`OnUnfocus`（游戏改版），动画会退化成简单淡入淡出。

## 已知限制

- 只删除**当前存档作用域**（`modded/profileN`）的记录，不会同步删除未打 mod 的 `profileN` 里同名条目。
- 不修改 `progress.save`、成就或统计页面；游戏本身也没有"删历史要回滚统计"的机制。
- 不支持批量删除 / 清空。
- 结算页的「查看本局」入口用的也是同一个 `NRunHistory`，按钮同样存在；但若该入口内部没有 submenu stack，删到最后一条时不会自动关闭页面（只记日志）。

## 仓库结构

```
.
├─ LICENSE
├─ README.md
├─ build.ps1
├─ .gitignore
├─ HistoryDeleter/
│  ├─ HistoryDeleter.csproj
│  ├─ Local.props.example      # 复制为 Local.props 填本机游戏路径（后者不入库）
│  ├─ HistoryDeleter.json      # mod manifest（id / 依赖 RitsuLib）
│  └─ src/
│     ├─ Entry.cs                       # [ModInitializer] 入口，创建 RitsuLib patcher
│     ├─ RunHistoryAccess.cs            # 反射访问 _runNames / _index / RefreshAndSelectRun / _stack
│     ├─ RunHistoryDeleteService.cs     # 走 ISaveStore 删除 .run + .run.backup
│     ├─ Patches/RunHistoryPatches.cs   # 2 个 IPatchMethod
│     └─ Ui/
│        ├─ DeleteRunButton.cs          # 原版按钮复用、悬浮动画、确认弹窗、删除后刷新
│        ├─ ModLoc.cs                   # 运行时合并弹窗文案到 main_menu_ui
│        └─ UiText.cs                   # 悬浮/反馈文字（跟随游戏语言）
└─ tools/
   └─ ContractCheck/                    # 游戏更新后自检（可选，不参与 mod 构建）
```

## 开发工具：ContractCheck

游戏更新可能改私有字段名。`tools/ContractCheck` 用 `MetadataLoadContext` **只读**检查 mod 依赖的 32 个成员是否还在、类型是否正确（不启动游戏、不碰存档）：

```powershell
cd tools/ContractCheck
dotnet run -- "D:\Steam\steamapps\common\Slay the Spire 2"
# 或
$env:STS2_DIR = "D:\Steam\steamapps\common\Slay the Spire 2"; dotnet run
```

覆盖范围：历史页私有字段/方法、`SaveManager._saveStore`、`ISaveStore`、`NGenericPopup` / `NModalContainer`、`LocManager` / `LocTable`、`NDeleteProfileButton.OnFocus/OnUnfocus`、`MegaLabel.SetTextAutoSize`。

对 `sts2.dll` v0.113.1 的结果为 32/32 通过。

反编译游戏源码（进一步排查时用，需要 `ilspycmd`）：

```powershell
ilspycmd -p -o <输出目录> --nested-directories "<游戏目录>\data_sts2_windows_x86_64\sts2.dll"
```

## 安全与免责

- 删除是不可撤销的：mod 只在你于确认弹窗点「确认」后删除文件，且删除后无法恢复（除非从 Steam 云备份或系统备份找回）。第一次使用前建议手动备份 `history` 文件夹。
- 代码中的防呆：
  1. 必须经原生确认弹窗确认；
  2. 只接受裸文件名且必须以 `.run` 结尾，含路径分隔符 / `..` / 盘符的直接拒绝；
  3. 删除后再次 `FileExists` 校验，文件仍在则报失败且不移除列表项；
  4. 复用的原版删档按钮被设为 `MouseFilter=Ignore` + `FocusMode=None`，永远收不到点击。
- 本项目与 Mega Crit 无关。使用本 mod 造成的数据丢失由使用者自行承担。

## License

[MIT](LICENSE)
