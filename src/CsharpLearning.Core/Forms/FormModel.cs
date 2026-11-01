namespace CsharpLearning.Core.Forms;

public enum ControlKind { Label, Button, TextBox, CheckBox, ListBox, PictureBox, ProgressBar }

/// <summary>
/// Что делает действие. Из одного и того же описания получается и живой макет окна в приложении,
/// и настоящий код WinForms — поэтому ученик видит ровно то, что напишет сам.
/// </summary>
public enum ActionKind
{
    SetText, SetVar, Inc, AddItem, ClearItems, SetVisible, SetEnabled, SetPicture, SetProgress, Message, If, For, Try,
}

public sealed class ActionSpec
{
    public ActionKind Kind { get; set; }
    /// <summary>Имя элемента или переменной, над которой действие.</summary>
    public string Target { get; set; } = "";
    /// <summary>Значение: шаблон текста с {элемент}, {элемент:int}, {переменная}, {now:HH:mm}, {=a + b}.</summary>
    public string Value { get; set; } = "";
    /// <summary>Условие для If: «ctrl.IsEmpty», «ctrl.Checked», «ctrl.NotInt», «left op right».</summary>
    public string Condition { get; set; } = "";
    public string From { get; set; } = "1";
    public string To { get; set; } = "5";
    public List<ActionSpec> Then { get; set; } = new();
    public List<ActionSpec> Else { get; set; } = new();
    public List<ActionSpec> Body { get; set; } = new();
    public List<ActionSpec> Catch { get; set; } = new();
}

public sealed class HandlerSpec
{
    /// <summary>Click, TextChanged, CheckedChanged.</summary>
    public string Event { get; set; } = "Click";
    /// <summary>Имя метода-обработчика (пусть выглядит так, как его назвал бы Visual Studio).</summary>
    public string Name { get; set; } = "";
    public List<ActionSpec> Actions { get; set; } = new();
}

public sealed class ControlSpec
{
    public string Name { get; set; } = "";
    public ControlKind Kind { get; set; }
    public string Text { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 100;
    public int H { get; set; } = 28;
    public List<string> Items { get; set; } = new();
    public string Picture { get; set; } = "";
    public bool Checked { get; set; }
    public int Value { get; set; }
    public bool Visible { get; set; } = true;
    public List<HandlerSpec> Handlers { get; set; } = new();
}

public sealed class StateVar
{
    public string Name { get; set; } = "";
    /// <summary>int, double или string.</summary>
    public string Type { get; set; } = "int";
    public string Initial { get; set; } = "0";
}

public sealed class FormSpec
{
    public string Title { get; set; } = "Form1";
    public int Width { get; set; } = 420;
    public int Height { get; set; } = 260;
    public List<ControlSpec> Controls { get; set; } = new();
    public List<StateVar> State { get; set; } = new();

    public ControlSpec? Find(string name) => Controls.FirstOrDefault(c => c.Name == name);
}

/// <summary>Готовый учебный пример: окно + пояснения, чему он учит.</summary>
public sealed class FormExample
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Чему учит этот пример (одна-две фразы).</summary>
    public string Lesson { get; set; } = "";
    /// <summary>Теги уроков (TagMapper), к которым пример подходит.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
    /// <summary>Пошаговый разбор: что происходит при нажатии.</summary>
    public List<string> Steps { get; set; } = new();
    public FormSpec Form { get; set; } = new();
    /// <summary>Подсказка, что попробовать в окне.</summary>
    public string TryIt { get; set; } = "";
}
