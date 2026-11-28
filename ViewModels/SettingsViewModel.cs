using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Services;

namespace GameServerKeeper.ViewModels;

/// <summary>Window 를 닫아 달라고 요청하는 대화상자 ViewModel 의 기반</summary>
public abstract class DialogViewModel : ObservableObject
{
    public event Action<bool>? CloseRequested;
    protected void Close(bool result) => CloseRequested?.Invoke(result);
}

/// <summary>설정 화면의 한 섹션 (Category)</summary>
public sealed partial class SettingSectionViewModel(string name, IReadOnlyList<SettingItemViewModel> items) : ObservableObject
{
    public string Name { get; } = name;
    public IReadOnlyList<SettingItemViewModel> Items { get; } = items;

    /// <summary>게임이 지원하지 않아 숨긴 항목이 있을 때 안내 문구</summary>
    [ObservableProperty] private string? _hint;
}

/// <summary>
/// 설정 화면. 설정 클래스의 특성(Category/DisplayName/Description/Range/PathEditor/RequiresCapability)을 읽어
/// 항목 ViewModel 을 자동으로 만든다. → 새 게임의 전용 설정도 클래스만 만들면 화면이 자동 생성됨.
/// 호출 측이 복제본을 넘기므로 [취소] 시 원본은 바뀌지 않는다.
/// </summary>
public sealed partial class SettingsViewModel : DialogViewModel
{
    private readonly object _target;
    private readonly Func<string> _subtitleFactory;
    private readonly Func<List<string>> _validate;
    private readonly Func<GameCapabilities>? _capabilities;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    /// <param name="subtitle">제목 아래 안내 문구 (게임 종류를 바꾸면 다시 계산)</param>
    /// <param name="capabilities">현재 설정 기준 게임 지원 기능 (지원하지 않는 기능의 항목은 숨김)</param>
    /// <param name="allowGameChange">서버 프로필 편집 시 게임 종류 변경 허용 여부 (실행 중인 서버는 false)</param>
    public SettingsViewModel(string title, Func<string> subtitle, object target, Func<List<string>> validate,
        IDialogService dialogs, Func<GameCapabilities>? capabilities = null, bool allowGameChange = true)
    {
        Title = title;
        _target = target;
        _subtitleFactory = subtitle;
        _validate = validate;
        _capabilities = capabilities;
        Dialogs = dialogs;
        AllowGameChange = allowGameChange;
        Build();
    }

    public string Title { get; }
    public bool AllowGameChange { get; }
    internal IDialogService Dialogs { get; }

    public ObservableCollection<SettingSectionViewModel> Sections { get; } = new();

    [ObservableProperty] private SettingSectionViewModel? _selectedSection;
    [ObservableProperty] private string _subtitle = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasErrors))]
    private string? _errorText;

    public bool HasErrors => ErrorText != null;

    // ═════════════ 항목 만들기 ═════════════

    /// <summary>게임 종류가 바뀌면 게임 전용 항목이 달라지므로 전체를 다시 만든다 (값은 대상 객체에 있어서 유지됨)</summary>
    private void Build()
    {
        var selected = SelectedSection?.Name;
        ErrorText = null; // 항목이 바뀌었으므로 이전 오류 안내는 지움
        Sections.Clear();
        foreach (var group in Enumerate(_target, null).GroupBy(i => i.Category))
            Sections.Add(new SettingSectionViewModel(group.Key, group.ToList()));
        SelectedSection = Sections.FirstOrDefault(s => s.Name == selected) ?? Sections.FirstOrDefault();
        Subtitle = _subtitleFactory();
        RefreshCapabilities();
    }

    private IEnumerable<SettingItemViewModel> Enumerate(object owner, string? forcedCategory)
    {
        foreach (var p in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false) continue;
            var category = forcedCategory ?? p.GetCustomAttribute<CategoryAttribute>()?.Category ?? "기타";

            // 게임 전용 설정은 펼쳐서 같은 화면에 표시
            if (typeof(GameSettings).IsAssignableFrom(p.PropertyType))
            {
                // 서버 프로필이면 맨 위에 "게임 종류" 선택 항목
                if (owner is ServerProfile profile)
                    yield return new GameSelectorItem(this, profile, category);

                if (p.GetValue(owner) is { } nested)
                    foreach (var inner in Enumerate(nested, category)) yield return inner;
                continue;
            }
            if (!p.CanWrite || p.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly == true) continue;

            var display = p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name;
            var description = p.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var requires = p.GetCustomAttribute<RequiresCapabilityAttribute>()?.Capability;
            var type = p.PropertyType;

            if (type == typeof(bool)) yield return new BoolSettingItem(this, owner, p, category, display, description, requires);
            else if (type == typeof(int) || type == typeof(double)) yield return new NumberSettingItem(this, owner, p, category, display, description, requires);
            else if (type.IsEnum) yield return new EnumSettingItem(this, owner, p, category, display, description, requires);
            else if (type == typeof(string)) yield return new TextSettingItem(this, owner, p, category, display, description, requires);
        }
    }

    // ═════════════ 항목에서 호출 ═════════════

    internal void OnValueChanged(SettingItemViewModel item)
    {
        if (item.Owner is GameSettings) RefreshCapabilities(); // 게임 설정이 바뀌면 지원 기능이 달라질 수 있음
    }

    internal void ChangeGame(ServerProfile profile, GameDefinition next)
    {
        if (string.Equals(next.GameId, profile.GameId, StringComparison.OrdinalIgnoreCase)) return;
        var prev = GameRegistry.Find(profile.GameId);
        profile.Game = (GameSettings)Activator.CreateInstance(next.SettingsType)!;
        // App ID 가 비어 있거나 이전 게임의 기본값 그대로면 새 게임 기본값으로
        if (profile.AppId == 0 || profile.AppId == prev?.DefaultAppId) profile.AppId = next.DefaultAppId;

        // 게임 선택 콤보박스 자신도 다시 만들어지므로 선택 처리가 끝난 뒤 실행
        if (_ui != null) _ui.Post(_ => Build(), null);
        else Build();
    }

    /// <summary>게임이 지원하지 않는 기능의 항목은 숨기고, 섹션 위에 안내 문구 표시</summary>
    private void RefreshCapabilities()
    {
        if (_capabilities == null) return;
        GameCapabilities caps;
        try { caps = _capabilities(); }
        catch { return; }

        foreach (var section in Sections)
        {
            int hidden = 0;
            foreach (var item in section.Items)
            {
                if (item.Requires is not GameCapabilities req) continue;
                item.IsVisible = (caps & req) == req;
                if (!item.IsVisible) hidden++;
            }
            section.Hint = hidden > 0 ? $"이 게임은 인게임 공지나 접속자 조회를 지원하지 않아서 관련 항목 {hidden}개를 숨겼어요." : null;
        }
    }

    // ═════════════ 저장 / 취소 ═════════════

    [RelayCommand]
    private void Save()
    {
        var errors = Sections.SelectMany(s => s.Items).OfType<NumberSettingItem>()
            .Where(n => n.HasError).Select(n => $"{n.Display}: {n.RangeText} 범위의 숫자를 입력해 주세요.")
            .Concat(_validate()).ToList();
        if (errors.Count > 0)
        {
            ErrorText = "저장하지 못했어요. 아래 내용을 확인해 주세요.\n" + string.Join("\n", errors.Select(x => "•  " + x));
            return;
        }
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}

// ═════════════════════════ 항목 ═════════════════════════

/// <summary>설정 항목 하나. 값은 대상 객체의 속성을 직접 읽고 쓴다.</summary>
public abstract partial class SettingItemViewModel : ObservableObject
{
    protected SettingItemViewModel(SettingsViewModel parent, object owner, PropertyInfo? prop,
        string category, string display, string? description, GameCapabilities? requires)
    {
        Parent = parent;
        Owner = owner;
        Prop = prop;
        Category = category;
        Display = display;
        Description = description;
        Requires = requires;
    }

    protected SettingsViewModel Parent { get; }
    public object Owner { get; }
    protected PropertyInfo? Prop { get; }
    public string Category { get; }
    public string Display { get; }
    public string? Description { get; }
    public GameCapabilities? Requires { get; }
    public string? ToolTip => string.IsNullOrWhiteSpace(Description) ? null : $"{Display}\n{Description}";

    [ObservableProperty] private bool _isVisible = true;

    protected object? Read() => Prop!.GetValue(Owner);

    protected void Write(object? value)
    {
        Prop!.SetValue(Owner, value);
        Parent.OnValueChanged(this);
    }
}

public sealed class BoolSettingItem(SettingsViewModel parent, object owner, PropertyInfo prop,
    string category, string display, string? description, GameCapabilities? requires)
    : SettingItemViewModel(parent, owner, prop, category, display, description, requires)
{
    public bool Value
    {
        get => (bool)Read()!;
        set { Write(value); OnPropertyChanged(); }
    }
}

/// <summary>정수/실수. 잘못 입력하면 값은 바꾸지 않고 오류 표시만 한다.</summary>
public sealed partial class NumberSettingItem : SettingItemViewModel
{
    private readonly bool _isDouble;
    private readonly double _min, _max;

    public NumberSettingItem(SettingsViewModel parent, object owner, PropertyInfo prop,
        string category, string display, string? description, GameCapabilities? requires)
        : base(parent, owner, prop, category, display, description, requires)
    {
        _isDouble = prop.PropertyType == typeof(double);
        var range = prop.GetCustomAttribute<RangeAttribute>();
        _min = range != null ? Convert.ToDouble(range.Minimum) : 0;
        _max = range != null ? Convert.ToDouble(range.Maximum) : int.MaxValue;
        _text = Convert.ToDouble(Read()).ToString(_isDouble ? "0.0##" : "0", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string RangeText => _max >= int.MaxValue ? $"{_min:0.##} 이상" : $"{_min:0.##} ~ {_max:0.##}";

    [ObservableProperty] private string _text;
    [ObservableProperty] private bool _hasError;

    partial void OnTextChanged(string value)
    {
        var ok = double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                 && v >= _min && v <= _max && (_isDouble || v == Math.Floor(v));
        HasError = !ok;
        if (ok) Write(_isDouble ? v : (object)(int)v);
    }
}

public sealed record EnumOption(object Value, string Text);

public sealed class EnumSettingItem : SettingItemViewModel
{
    public EnumSettingItem(SettingsViewModel parent, object owner, PropertyInfo prop,
        string category, string display, string? description, GameCapabilities? requires)
        : base(parent, owner, prop, category, display, description, requires)
    {
        var type = prop.PropertyType;
        Options = Enum.GetValues(type).Cast<object>()
            .Select(v => new EnumOption(v, type.GetField(v.ToString()!)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? v.ToString()!))
            .ToList();
    }

    public IReadOnlyList<EnumOption> Options { get; }

    public EnumOption? Selected
    {
        get => Options.FirstOrDefault(o => Equals(o.Value, Read()));
        set { if (value != null) { Write(value.Value); OnPropertyChanged(); } }
    }
}

/// <summary>문자열. 경로면 "찾아보기", 비밀번호면 "보기/숨기기" 버튼이 붙는다.</summary>
public sealed partial class TextSettingItem : SettingItemViewModel
{
    private readonly PathEditorAttribute? _path;

    public TextSettingItem(SettingsViewModel parent, object owner, PropertyInfo prop,
        string category, string display, string? description, GameCapabilities? requires)
        : base(parent, owner, prop, category, display, description, requires)
    {
        _path = prop.GetCustomAttribute<PathEditorAttribute>();
        IsPassword = prop.GetCustomAttribute<PasswordPropertyTextAttribute>()?.Password == true;
    }

    public string Value
    {
        get => Read() as string ?? "";
        set { Write(value); OnPropertyChanged(); }
    }

    public bool IsPassword { get; }
    public bool HasBrowse => _path != null;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowPlainText), nameof(ShowPasswordBox))]
    private bool _isRevealed;

    public bool ShowPlainText => !IsPassword || IsRevealed;
    public bool ShowPasswordBox => IsPassword && !IsRevealed;

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;

    [RelayCommand]
    private void Browse()
    {
        if (_path == null) return;
        var picked = _path.Kind == PathKind.Folder
            ? Parent.Dialogs.PickFolder(Display, Value)
            : Parent.Dialogs.PickFile(Display, _path.Filter, Value);
        if (picked != null) Value = picked;
    }
}

/// <summary>서버 프로필의 "게임 종류"</summary>
public sealed class GameSelectorItem : SettingItemViewModel
{
    private readonly ServerProfile _profile;

    public GameSelectorItem(SettingsViewModel parent, ServerProfile profile, string category)
        : base(parent, profile, null, category, "게임 종류",
            parent.AllowGameChange
                ? "이 서버가 어떤 게임인지예요. 게임마다 지원하는 기능(공지·저장·접속자 조회)과 아래 전용 항목이 달라져요. 실행 파일로 자동 인식했지만 틀렸다면 여기서 바꾸세요."
                : "이 서버가 어떤 게임인지예요. 서버가 실행 중일 때는 바꿀 수 없어요. 먼저 서버를 종료하세요.",
            null)
    {
        _profile = profile;
    }

    public IReadOnlyList<GameDefinition> Games => GameRegistry.All;
    public bool CanChange => Parent.AllowGameChange;

    public GameDefinition? SelectedGame
    {
        get => GameRegistry.Find(_profile.GameId);
        set { if (value != null) Parent.ChangeGame(_profile, value); }
    }
}
