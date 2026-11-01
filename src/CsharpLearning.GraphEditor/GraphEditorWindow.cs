using System.Windows;
using CsharpLearning.Core.Graph;

namespace CsharpLearning.GraphEditor;

/// <summary>Отдельное окно с редактором графов.</summary>
public sealed class GraphEditorWindow : Window
{
    public GraphEditorWindow(GraphProject? project = null)
    {
        Title = "Граф-режим — визуальное программирование на C#";
        Width = 1500; Height = 900; MinWidth = 1000; MinHeight = 600;
        Background = CodeRender.Solid("#FF1B1D24");
        UseLayoutRounding = true;

        var view = new GraphEditorView();
        Content = view;
        if (project is not null) Loaded += (s, e) => view.Open(project);
    }
}
