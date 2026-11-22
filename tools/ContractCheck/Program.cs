using System.Reflection;

// Contract check: verifies that every game member the mod touches through reflection still exists
// (and has the expected shape) in the shipped sts2.dll. Run it after a game update.
//
//   dotnet run -- "<游戏目录>"                     # data_sts2_windows_x86_64 is detected automatically
//   dotnet run -- "<游戏目录>\data_sts2_windows_x86_64"
//   STS2_DIR="<游戏目录>" dotnet run
//
// Nothing is written: the game assembly is only read as metadata.

var dataDir = ResolveDataDir(args);
if (dataDir == null)
{
    Console.Error.WriteLine("缺少游戏目录。用法：dotnet run -- \"<游戏目录>\"，或设置环境变量 STS2_DIR。");
    return 2;
}

var sts2Path = Path.Combine(dataDir, "sts2.dll");
if (!File.Exists(sts2Path))
{
    Console.Error.WriteLine($"在 {dataDir} 下找不到 sts2.dll。请指向游戏根目录或 data_sts2_windows_x86_64。");
    return 2;
}

static string? ResolveDataDir(string[] args)
{
    var candidate = args.Length > 0
        ? args[0]
        : Environment.GetEnvironmentVariable("STS2_DIR");

    if (string.IsNullOrWhiteSpace(candidate))
    {
        return null;
    }

    candidate = candidate.Trim().Trim('"');
    if (File.Exists(Path.Combine(candidate, "sts2.dll")))
    {
        return candidate;
    }

    var nested = Path.Combine(candidate, "data_sts2_windows_x86_64");
    return File.Exists(Path.Combine(nested, "sts2.dll")) ? nested : candidate;
}

// The game's data folder ships a complete .NET runtime, so resolving everything from there avoids
// mixing two different copies of the same framework assembly.
var paths = Directory.GetFiles(dataDir, "*.dll")
    .Append(sts2Path)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths));
var asm = mlc.LoadFromAssemblyPath(sts2Path);
var core = mlc.CoreAssembly!;

var failures = 0;
var checks = 0;

void Check(string label, bool ok, string? detail = null)
{
    checks++;
    if (ok)
    {
        Console.WriteLine($"  PASS  {label}");
        return;
    }

    failures++;
    Console.WriteLine($"  FAIL  {label}{(detail is null ? "" : " -> " + detail)}");
}

Type? Find(string fullName) => asm.GetType(fullName, throwOnError: false);

const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
const BindingFlags Any = Inst | Static;

bool IsType(object? type, string fullName) => type is Type t && t.FullName == fullName;

var int32 = core.GetType("System.Int32")!;
var boolean = core.GetType("System.Boolean")!;
var stringType = core.GetType("System.String")!;
var voidType = core.GetType("System.Void")!;

FieldInfo? Field(Type? type, string name) => type?.GetField(name, Any);
MethodInfo? Method(Type? type, string name, params Type[] parameters) =>
    type?.GetMethod(name, Inst, null, parameters, null);

Console.WriteLine("NRunHistory (run history screen)");
var nrh = Find("MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen.NRunHistory");
Check("type exists", nrh != null);
Check("inherits NSubmenu", nrh?.BaseType?.Name == "NSubmenu", nrh?.BaseType?.FullName);

var runNames = Field(nrh, "_runNames");
Check(
    "_runNames : List<string>",
    runNames != null &&
    runNames.FieldType.IsGenericType &&
    runNames.FieldType.GetGenericTypeDefinition().FullName == "System.Collections.Generic.List`1" &&
    runNames.FieldType.GetGenericArguments()[0].FullName == "System.String",
    runNames?.FieldType.FullName);

var index = Field(nrh, "_index");
Check("_index : int", IsType(index?.FieldType, "System.Int32"), index?.FieldType.FullName);

var refresh = nrh?.GetMethod("RefreshAndSelectRun", Inst, null, [int32], null);
Check("RefreshAndSelectRun(int) : Task", refresh != null && refresh.ReturnType.Name == "Task",
    refresh?.ReturnType.FullName);

Check("_Ready()", nrh?.GetMethod("_Ready", Inst, null, Type.EmptyTypes, null) != null);
Check("OnSubmenuOpened()", nrh?.GetMethod("OnSubmenuOpened", Inst, null, Type.EmptyTypes, null) != null);

var canBeShown = nrh?.GetMethod("CanBeShown", Static, null, Type.EmptyTypes, null);
Check("static CanBeShown() : bool", canBeShown != null && canBeShown.ReturnType.FullName == "System.Boolean");

Console.WriteLine("NSubmenu (submenu stack)");
var submenu = Find("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSubmenu");
var stack = Field(submenu, "_stack");
Check("_stack : NSubmenuStack",
    stack?.FieldType.FullName == "MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSubmenuStack",
    stack?.FieldType.FullName);

Console.WriteLine("SaveManager (save store)");
var saveManager = Find("MegaCrit.Sts2.Core.Saves.SaveManager");
var saveStore = Field(saveManager, "_saveStore");
Check("_saveStore : ISaveStore",
    saveStore?.FieldType.FullName == "MegaCrit.Sts2.Core.Saves.ISaveStore",
    saveStore?.FieldType.FullName);

var instance = saveManager?.GetProperty("Instance", Static);
Check("static Instance property", instance != null);

var currentProfileId = saveManager?.GetProperty("CurrentProfileId", Inst);
Check("CurrentProfileId : int", IsType(currentProfileId?.PropertyType, "System.Int32"),
    currentProfileId?.PropertyType.FullName);

Console.WriteLine("RunHistorySaveManager / ISaveStore");
var historyManager = Find("MegaCrit.Sts2.Core.Saves.Managers.RunHistorySaveManager");
var getHistoryPath = historyManager?.GetMethod("GetHistoryPath", Static, null,
    [int32, core.GetType("System.Nullable`1")!.MakeGenericType(boolean)], null);
Check("static GetHistoryPath(int, bool?) : string",
    getHistoryPath != null && getHistoryPath.ReturnType.FullName == "System.String");

var saveStoreInterface = Find("MegaCrit.Sts2.Core.Saves.ISaveStore");
Check("ISaveStore.DeleteFile(string)", Method(saveStoreInterface, "DeleteFile", stringType) != null);
Check("ISaveStore.FileExists(string)", Method(saveStoreInterface, "FileExists", stringType) != null);

var cloudStore = Find("MegaCrit.Sts2.Core.Saves.CloudSaveStore");
Check("CloudSaveStore implements ISaveStore",
    cloudStore != null && saveStoreInterface != null &&
    saveStoreInterface.IsAssignableFrom(cloudStore));
Check("CloudSaveStore.DeleteFile(string) overrides the cloud path",
    cloudStore?.GetMethod("DeleteFile", Inst, null, [stringType], null) != null);

Console.WriteLine("Confirmation popup (same flow as the vanilla quit confirmation)");
var localisation = Find("MegaCrit.Sts2.Core.Localization.LocString");
var locStringType = localisation;
Check("LocString(string, string) constructor",
    localisation?.GetConstructor(Inst, null, [stringType, stringType], null) != null);

var popup = Find("MegaCrit.Sts2.Core.Nodes.Multiplayer.NGenericPopup");
var create = popup?.GetMethod("Create", Static, null, Type.EmptyTypes, null);
Check("static NGenericPopup.Create()", create != null && create.ReturnType == popup);

// Nullable reference types do not change the runtime parameter type: noButton is still LocString.
var wait = popup?.GetMethod("WaitForConfirmation", Inst, null, [localisation!, localisation!, localisation!, localisation!], null);
Check("NGenericPopup.WaitForConfirmation(body, header, no, yes) : Task<bool>",
    wait != null &&
    wait.ReturnType.IsGenericType &&
    wait.ReturnType.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.Task`1" &&
    IsType(wait.ReturnType.GetGenericArguments()[0], "System.Boolean"),
    wait?.ReturnType.FullName);

var modalContainer = Find("MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer");
Check("static NModalContainer.Instance", modalContainer?.GetProperty("Instance", Static) != null);
Check("public NModalContainer.OpenModal", modalContainer?.GetProperty("OpenModal", Inst) != null);
Check("NModalContainer.Add(Node, bool)", modalContainer?.GetMethods(Inst)
    .Any(m => m.Name == "Add" && m.GetParameters().Length == 2) == true);

Console.WriteLine("Localization merge (custom dialog text without a .pck)");
var locManager = Find("MegaCrit.Sts2.Core.Localization.LocManager");
var getTable = locManager?.GetMethod("GetTable", Inst, null, [stringType], null);
var locTable = Find("MegaCrit.Sts2.Core.Localization.LocTable");
Check("static LocManager.Instance", locManager?.GetProperty("Instance", Static) != null);
Check("LocManager.Language : string", IsType(locManager?.GetProperty("Language", Inst)?.PropertyType, "System.String"));
Check("LocManager.GetTable(string) : LocTable",
    getTable != null && getTable.ReturnType == locTable);
Check("LocTable.MergeWith(Dictionary<string,string>)",
    locTable?.GetMethods(Inst).Any(m => m.Name == "MergeWith" && m.GetParameters().Length == 1) == true);

var taskHelper = Find("MegaCrit.Sts2.Core.Helpers.TaskHelper");
Check("TaskHelper.RunSafely(Task)", taskHelper?.GetMethods(Static)
    .Any(m => m.Name == "RunSafely" && m.GetParameters().Length == 1) == true);

Console.WriteLine("Reused profile delete button (visuals + hover animation)");
var deleteProfileButton = Find("MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen.NDeleteProfileButton");
Check("NDeleteProfileButton type exists", deleteProfileButton != null);
Check("NDeleteProfileButton.OnFocus()", deleteProfileButton?.GetMethods(Inst)
    .Any(m => m.Name == "OnFocus" && m.GetParameters().Length == 0) == true);
Check("NDeleteProfileButton.OnUnfocus()", deleteProfileButton?.GetMethods(Inst)
    .Any(m => m.Name == "OnUnfocus" && m.GetParameters().Length == 0) == true);

var megaLabel = Find("MegaCrit.Sts2.addons.mega_text.MegaLabel");
Check("MegaLabel.SetTextAutoSize(string)", megaLabel?.GetMethods(Inst)
    .Any(m => m.Name == "SetTextAutoSize" && m.GetParameters().Length == 1) == true);

Console.WriteLine();
Console.WriteLine($"{checks - failures}/{checks} checks passed.");
return failures == 0 ? 0 : 1;

