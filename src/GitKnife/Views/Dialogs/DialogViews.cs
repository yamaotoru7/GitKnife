using System.Windows;
using GitKnife.Controls;
using GitKnife.ViewModels.Dialogs;
using static GitKnife.Views.Dialogs.Form;

namespace GitKnife.Views.Dialogs;

/// <summary>小さなダイアログの本文。</summary>
internal static class DialogViews
{
    public static FrameworkElement For(DialogViewModel vm) => vm switch
    {
        FetchDialogViewModel => new FetchDialogView(),
        PullDialogViewModel => new PullDialogView(),
        PushDialogViewModel => new PushDialogView(),
        FinishBranchDialogViewModel => new Form()
            .Row("Merge:", Value(Icon("Icon.Branch"), "Branch"))
            .Row("Into:", Value(Icon("Icon.Branch"), "Main")),
        StartBranchDialogViewModel => new Form()
            .Row("Start branch on:", Value(Icon("Icon.Branch"), "MainBranch"))
            .Row("Branch name:", Text("BranchNameTextBox", "BranchName").Hint("Enter branch name", "Icon.Branch"))
            .Row("Local changes:", LocalChangesRadios(), 78),
        SaveSnapshotDialogViewModel => new Form()
            .Row("Message:", Text("StashMessageTextBox", "Message").Hint("Stash message (optional)", "Icon.Stash"))
            .Row(null, Check("StageNewFilesCheckBox", "Stage new files", "StageNewFiles"), 26)
            .Row(null, StageNewFilesNote(), 22),
        CreateBranchDialogViewModel => new Form()
            .Row("Create branch at:", Value(Icon("Icon.Branch"), "Start.Display"))
            .Row("Branch name:", Text("BranchNameTextBox", "BranchName").Hint("Enter branch name", "Icon.Branch"))
            .Row(null, Check("CheckoutAfterCreateCheckBox", "Check out after create", "Checkout"), 26)
            .Row("Local changes:", LocalChangesRadios(), 78, visibleWhen: "ShowLocalChanges"),
        CheckoutDialogViewModel => new Form()
            .Row("Switch to:", Value(Icon("Icon.Branch"), "SwitchTo"))
            .Row("Local changes:", LocalChangesRadios(), 78),
        CreateTagDialogViewModel => new Form()
            .Row("Create tag at:", Value(Icon("Icon.Branch"), "Target.Display"))
            .Row("Tag Name:", Text("TagNameTextBox", "TagName").Hint("Enter tag name", "Icon.Tag"))
            .Row("Message:", MultiLine("TagMessageTextBox", "Message", 100).Hint("Enter tag message (optional)"), 108)
            .Row(null, Check("PushCheckBox", "Push", "Push").With(c => c.SetBinding(System.Windows.Controls.ContentControl.ContentProperty, new System.Windows.Data.Binding("PushText"))), 26),
        DeleteBranchDialogViewModel => new Form()
            .Row("Branch:", Value(Icon("Icon.Branch"), "BranchName"))
            // upstream が無くても無効の状態で出す
            .Row(null, Horizontal(Check("DeleteRemoteBranchCheckBox", "Also delete corresponding remote branch", "DeleteRemote").With(c =>
                c.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding("HasUpstream"))), Value(null, "UpstreamName", "DeleteRemoteBranchCheckBoxUpstream")), 26)
            .Row(null, Horizontal(WorktreeWarning(), Check("DeleteWorktreeCheckBox", "Also delete worktree", "DeleteWorktree"), Value(Icon("Icon.Folder"), "WorktreeName", "DeleteWorktreeLabel")), 26, visibleWhen: "HasWorktree"),
        DeleteBranchesDialogViewModel => new Form()
            .Row("Branches:", ItemsList("BranchNames", "Icon.Branch", 112), 120)
            .Row(null, Check("DeleteRemoteBranchCheckBox", "Also delete corresponding remote branches", "DeleteRemote").With(c =>
                c.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding("HasUpstreams"))), 26),
        MultiPushDialogViewModel => new Form().Row("Branches:", ItemsList("BranchNames", "Icon.Branch", 129), 136),
        AddSubmoduleDialogViewModel => new Form()
            .Row("Repository URL:", Text("RepositoryUrlTextBox", "Url").Hint("Git Repository Url", "Icon.Link"))
            .Row("Folder:", Text("PathTextBox", "Folder").Hint("Relative Local Path", "Icon.Folder"))
            .Row(null, Check("FetchNestedSubmodulesCheckBox", "Fetch nested submodules", "FetchNested"), 26),
        // 見出しの下に 2（-6 の余白と合わせて 8）
        ConfirmDialogViewModel => new System.Windows.Controls.Border { Height = 8 },
        SharedCustomCommandConfirmDialogViewModel => new Form(0) { Margin = new Thickness(80, 0, 0, 0) }
            .Row(null, Check("TrustThisRepositoryCheckBox", "", "Trust").With(c => c.SetBinding(System.Windows.Controls.ContentControl.ContentProperty, new System.Windows.Data.Binding("TrustText"))), 26),
        CustomCommandUiDialogViewModel cc => CustomCommandBody(cc),
        CustomActionResultDialogViewModel r => CustomActionResultBody(r.Output),
        EditCustomActionDialogViewModel => EditCustomActionBody(),
        EditCustomControlsDialogViewModel cv => EditCustomControlsBody(cv),
        GoToLineDialogViewModel => new Form().Row("Line number:", Text("LineNumberTextBox", "LineNumber")),
        RenameBranchDialogViewModel => new Form()
            .Row("New Name:", Text("BranchNameTextBox", "BranchName").Hint(null, "Icon.Branch"))
            .Row(null, Check("RenameRemoteBranchCheckbox", "", "RenameRemote").With(c => c.SetBinding(System.Windows.Controls.ContentControl.ContentProperty, new System.Windows.Data.Binding("RenameRemoteText"))), 26, visibleWhen: "HasUpstream"),
        DeleteTagDialogViewModel => new Form()
            .Row("Tag:", Value(Icon("Icon.Tag"), "TagName"))
            .Row(null, Check("DeleteFromRemotesCheckBox", "Delete tag from remote repositories", "DeleteFromRemotes"), 26),
        SaveStashDialogViewModel => new Form()
            .Row("Message:", Text("StashMessageTextBox", "Message").Hint("Stash message (optional)", "Icon.Stash"))
            .Row(null, Check("StageNewFilesCheckBox", "Stage new files", "StageNewFiles"), 26)
            .Row(null, StageNewFilesNote(), 22),
        PartialStashDialogViewModel => PartialStashBody(),
        CreatePatchDialogViewModel cp => RevisionsBody(cp.RevisionsLabel),
        RenameStashDialogViewModel => new Form().Row("Message:", Text("StashNameTextBox", "Message")),
        DeleteRemoteBranchDialogViewModel => new Form().Row("Branch:", Value(Icon("Icon.Branch"), "BranchText")),
        TagDetailsDialogViewModel => new Form()
            .Row("Tag:", Value(Icon("Icon.Tag"), "TagName"))
            .Row("Message:", MultiLine("MessageTextBox", "Message", 150).With(t => t.IsReadOnly = true), 160),
        CloneDialogViewModel c => CloneBody(c),
        AboutDialogViewModel => new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(80, 0, 0, 0),
            Children =
            {
                Value(null, "GitText", "GitVersionTextBlock").With(v => v.Margin = new Thickness(0)),
                Note("A Git client for Windows. Built with .NET, WPF and AvalonEdit.").With(n => n.Margin = new Thickness(0, 6, 0, 0)),
                Value(null, "Copyright", "CopyrightTextBlock").With(v => v.Margin = new Thickness(0, 6, 0, 0)),
            },
        },
        WorkspacesDialogViewModel w => WorkspacesBody(w),
        BugReportDialogViewModel b => BugReportBody(b),
        LfsTrackDialogViewModel => LfsTrackBody(),
        LfsRemoteDialogViewModel => new Form().Row("Remote:", Combo("RemotesComboBox", "Remotes", "SelectedRemote").With(c => c.ItemTemplate = (DataTemplate)Application.Current.FindResource("Gk.RemoteItemTemplate"))),
        LfsStatusDialogViewModel => LfsStatusBody(),
        ApplyPatchDialogViewModel => new Form().Row("Location:", FileBrowse("PathTextBox", "Path", "BrowseButton", "Git Patch (*.*)|*.*"), labelId: "LocationLabel")
            .Row(null, Check("CreateCommitsCheckBox", "Create commit", "CreateCommit"), 26, visibleWhen: "IsMailPatch"),
        CreateWorktreeDialogViewModel { RemoteBranch: not null } => new Form()
            .Row("Remote Branch:", Value(Icon("Icon.Branch"), "RemoteBranch"))
            .Row("Location:", FolderBrowse("PathTextBox", "Path")),
        CreateWorktreeDialogViewModel { IsNewBranch: false } => new Form()
            .Row("Local Branch:", Value(Icon("Icon.Branch"), "ExistingBranch"))
            .Row("Location:", FolderBrowse("PathTextBox", "Path")),
        CreateWorktreeDialogViewModel => new Form()
            .Row("Start from:", new Controls.RefPicker { Icon = Icon("Icon.Branch") }.Bind(Controls.RefPicker.ItemsProperty, "Branches").Bind(Controls.RefPicker.SelectedItemProperty, "StartFrom"))
            .Row("Branch name:", Text("BranchNameTextBox", "BranchName").Hint("Enter branch name", "Icon.Branch"))
            .Row("Location:", FolderBrowse("PathTextBox", "Path")),
        RemoteEditDialogViewModel r => RemoteBody(r),
        IgnorePatternDialogViewModel => IgnorePatternBody(),
        MultiCherryPickDialogViewModel => new System.Windows.Controls.StackPanel { Children =
        {
            RevisionsBody("Commits to apply:", "GitPointTextBlock"),
            new Form().Row(null, Check("CommitCheckBox", "Commit the changes", "CommitChanges"), 26)
                      .Row(null, Check("AppendOriginShaCheckBox", "Append origin to commit message", "AppendOrigin"), 26),
        } },
        RescanRepositoriesDialogViewModel => new Form().Row(null, Check("ResetDeletedCheckbox", "Reset previously deleted repository entries", "ResetDeleted"), 26),
        ApplyStashDialogViewModel => new Form()
            .Row("Stash to Apply:", Value(Icon("Icon.Stash"), "StashText"))
            .Row(null, Check("DeleteStashAfterApplyCheckBox", "Delete stash after applying", "DeleteAfterApply"), 26)
            .Row(null, Note("Stash will not be deleted if a conflict occurs"), 22),
        DeleteStashDialogViewModel => new Form()
            .Row("Stash:", Value(Icon("Icon.Stash"), "StashText")),
        MergeDialogViewModel => new Form()
            .Row("Merge:", Value(Icon("Icon.Branch"), "Source.Display"))
            .Row("Into:", Value(Icon("Icon.Branch"), "Into"))
            .Row("Merge Option:", MergeOptionCombo()),
        RebaseDialogViewModel => new Form()
            .Row("Rebase:", Value(Icon("Icon.Branch"), "Branch"), 26)
            .Row("On:", Value(Icon("Icon.Branch"), "Onto.Display"), 26)
            .Row(null, Check("UpdateRefsCheckBox", "Update dependent branches", "UpdateRefs"), 26, visibleWhen: "HasDependentBranches")
            .Row(null, ItemsList("DependentBranches", "Icon.Branch", 60).With(l => System.Windows.Automation.AutomationProperties.SetAutomationId(l, "LocalBranchesListBox")), 64, visibleWhen: "HasDependentBranches")
            .Row(null, Check("AutostashCheckBox", "Stash and reapply local changes", "Autostash"), 26),
        CherryPickDialogViewModel => new Form()
            .Row("Commit to apply:", Value(Icon("Icon.Commit"), "CommitText", "GitPointTextBlock"))
            .Row(null, Check("CommitCheckBox", "Commit the changes", "CommitChanges"), 26)
            .Row(null, Check("AppendOriginShaCheckBox", "Append origin to commit message", "AppendOrigin"), 26),
        RevertDialogViewModel => new Form()
            .Row("Commit to apply:", Value(Icon("Icon.Commit"), "CommitText"))
            .Row(null, Check("CommitCheckBox", "Commit the changes", "CommitChanges"), 26),
        ResetDialogViewModel => new Form()
            .Row("Branch:", Value(Icon("Icon.Branch"), "Branch"))
            .Row("Move to:", Value(Icon("Icon.Commit"), "CommitText"))
            .Row("Reset type:", ResetCombo()),
        CheckoutCommitDialogViewModel => new Form()
            .Row("Commit to checkout:", Value(Icon("Icon.Commit"), "CommitText"))
            .Row("Local changes:", LocalChangesRadios(), 78),
        ViewModels.InteractiveRebaseViewModel => new InteractiveRebaseView(),
        BenchmarkDialogViewModel b => BenchmarkView.Create(b),
        OpenRepositoryDialogViewModel o => new Form()
            .Row("Location:", LocationRow(o))
            .Row("Path:", RepositoryPathRow(o))
            .Row(null, RemoteFolders(), 164, visibleWhen: "IsRemote"),
        AddSshHostDialogViewModel a => new Form()
            .Row("Destination:", Text("SshHostDestinationTextBox", "Destination").Hint("user@host or a Host in ~/.ssh/config"))
            .Row("Name:", Text("SshHostNameTextBox", "Name").Hint("Optional. Defaults to the host name"))
            .Row("Git:", Text("SshHostGitTextBox", "GitPath").Hint("Optional. Defaults to git on PATH"))
            .Row(null, TestHostButton(a)),
        QuickCheckoutDialogViewModel => new Form()
            .Row("Branch:", new Controls.RefPicker { Icon = Icon("Icon.Branch") }.Bind(Controls.RefPicker.ItemsProperty, "Branches").Bind(Controls.RefPicker.SelectedItemProperty, "SelectedBranch")),
        MergeResolverViewModel r => new MergeResolverView(r),
        PreferencesViewModel p => new PreferencesView(p),
        RepositorySettingsViewModel s => new RepositorySettingsView(s),
        GitOutputDialogViewModel o => OutputEditor(o.Output),
        UpdateDialogViewModel u => ReleaseNotes(u.Notes),
        GitFlowInitDialogViewModel => new Form()
            .Row("Production Branch:", Text("MasterBranchTextBox", "MasterBranch"))
            .Row("Development Branch:", Text("DevelopBranchTextBox", "DevelopBranch"))
            .Row(null, new System.Windows.Controls.Border(), 23)   // ブランチと接頭辞の間を 1 行ぶん空ける
            .Row("Feature Prefix:", Text("FeaturePrefixTextBox", "FeaturePrefix"))
            .Row("Release Prefix:", Text("ReleasePrefixTextBox", "ReleasePrefix"))
            .Row("Hotfix Prefix:", Text("HotfixPrefixTextBox", "HotfixPrefix"))
            .Row("Version Tag Prefix:", Text("VersionTagPrefixTextBox", "VersionTagPrefix")),
        GitFlowStartDialogViewModel s => new Form()
            .Row(s.Prefix, Text($"{s.KindName}NameTextBox", "Name").Hint($"{s.KindName} Name"), labelId: $"{s.KindName}PrefixTextBlock")
            .Row("Start at:", new Controls.RefPicker { Icon = Icon("Icon.Branch") }.Bind(Controls.RefPicker.ItemsProperty, "Branches").Bind(Controls.RefPicker.SelectedItemProperty, "StartAt")),
        GitFlowFinishDialogViewModel f => GitFlowFinishBody(f),
        _ => throw new NotSupportedException(vm.GetType().Name),
    };

    /// <summary>
    /// Repository Url の欄の中の右端の "HTTPS ˅"（NetworkProtocolDropDownButton / NetworkProtocolTextBlock。Add Remote と Clone）。
    /// 押すと同じ URL の HTTPS の形と SSH の形を出し、選ぶと欄を置き換える。形が作れない URL（ローカルのパスなど）では何も出さない（推定）。
    /// </summary>
    private static FrameworkElement ProtocolUrl(System.Windows.Controls.TextBox box)
    {
        box.Padding = new Thickness(box.Padding.Left, box.Padding.Top, 60, box.Padding.Bottom);
        var label = new System.Windows.Controls.TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Gk.Accent");
        System.Windows.Automation.AutomationProperties.SetAutomationId(label, "NetworkProtocolTextBlock");
        var chevron = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse("M0,0 L3,3 L6,0"), StrokeThickness = 1.2, Margin = new Thickness(4, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Gk.Accent");
        var button = new System.Windows.Controls.Button { Content = Horizontal(label, chevron), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand };
        button.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Gk.FlatButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "NetworkProtocolDropDownButton");
        // 欄が空なら出さない（Clone を開いたとき）
        void Update()
        {
            label.Text = GitKnife.Core.Git.RemoteWebLinks.ProtocolName(box.Text);
            button.Visibility = box.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        box.TextChanged += (_, _) => Update();
        Update();
        button.Click += (_, _) =>
        {
            if (GitKnife.Core.Git.RemoteWebLinks.ProtocolForms(box.Text) is not { } forms) return;
            var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var url in new[] { forms.Https, forms.Ssh })
            {
                var item = new System.Windows.Controls.MenuItem { Header = url };
                item.Click += (_, _) => box.Text = url;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return new System.Windows.Controls.Grid { Children = { box, button } };
    }

    /// <summary>"Also delete worktree" にチェックを入れたときの警告の三角（チェックボックスの左、ラベルの欄に出す）。</summary>
    private static FrameworkElement WorktreeWarning()
    {
        var image = new System.Windows.Controls.Image { Source = (System.Windows.Media.ImageSource)Application.Current.FindResource("Gk.WarningImage"), Width = 16, Height = 15, Margin = new Thickness(-24, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(image, "WorktreeWarningImage");
        image.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("DeleteWorktree") { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        return image;
    }

    /// <summary>Finish Git Flow feature / release / hotfix：ブランチの選択、（release / hotfix は）タグのメッセージ、チェックボックス。</summary>
    private static FrameworkElement GitFlowFinishBody(GitFlowFinishDialogViewModel vm)
    {
        var form = new Form()
            .Row(vm.BranchLabel, new Controls.RefPicker { Icon = Icon("Icon.Branch") }.Bind(Controls.RefPicker.ItemsProperty, "Branches").Bind(Controls.RefPicker.SelectedItemProperty, "Branch"));
        if (vm.HasMessage) form.Row("Message:", MultiLine("TagMessageTextBox", "Message", 100).Hint(vm.MessagePlaceholder).WithoutAutoFocus(), 110);
        form.Row(null, Check("DeleteBranchesCheckBox", "Delete branches", "DeleteBranches"), 25);
        if (vm.IsFeature)
        {
            form.Row(null, Check("RebaseInsteadOfMergeCheckBox", "Rebase before merging", "Rebase"), 25);
            form.Row(null, Check("NoFastForwardCheckBox", "No fast-forward", "NoFastForward"), 25);
        }
        if (vm.IsRelease) form.Row(null, Check("BackMergeMasterCheckBox", "Back-merge master into develop", "BackMerge"), 25);
        return form;
    }

    /// <summary>
    /// カスタムコマンドの UI："Title:" のラベル + 欄。Text Box は TextBox、File Path は TextBox と "..."、
    /// Branch Selector はブランチのアイコン付きの ComboBox、Check Box はタイトルを文字にしたチェックボックス（ラベル列は空）。
    /// </summary>
    private static FrameworkElement CustomCommandBody(CustomCommandUiDialogViewModel vm)
    {
        var form = new Form();
        for (var i = 0; i < vm.Controls.Count; i++)
        {
            var c = vm.Controls[i];
            var at = $"Controls[{i}].";
            if (c.IsCheckBox)
                form.Row(null, Check("", c.Title, at + "IsChecked"), 26);
            else if (c.IsDropdown)
            {
                var combo = Combo("", at + "References", at + "SelectedReference");
                combo.ItemTemplate = BranchItemTemplate();
                form.Row(c.Title + ":", combo);
            }
            else if (c.IsFilePath)
            {
                var box = Text("", at + "Text");
                var browse = new System.Windows.Controls.Button { Content = "...", Width = 28, Height = 24, Margin = new Thickness(4, 0, 0, 0) };
                var control = c;
                browse.Click += (_, _) => { if (BrowsePath(control) is { } path) control.Text = path; };
                var dock = new System.Windows.Controls.DockPanel();
                System.Windows.Controls.DockPanel.SetDock(browse, System.Windows.Controls.Dock.Right);
                dock.Children.Add(browse);
                dock.Children.Add(box);
                form.Row(c.Title + ":", dock);
            }
            else
            {
                var box = Text("", at + "Text");
                if (c.Control.Placeholder.Length == 0) { form.Row(c.Title + ":", box); continue; }
                // 空のときの淡い文字（Save Stash の "Stash message (optional)" と同じ作り）
                var hint = new System.Windows.Controls.TextBlock { Text = c.Control.Placeholder, IsHitTestVisible = false, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                hint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Gk.Text.Dim");
                void Sync() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                box.TextChanged += (_, _) => Sync();
                box.Loaded += (_, _) => Sync();
                form.Row(c.Title + ":", new System.Windows.Controls.Grid { Children = { box, hint } });
            }
        }
        if (vm.Controls.Count == 0) return new System.Windows.Controls.Border { Height = 6 };
        return form;
    }

    /// <summary>選択肢の ComboBox（値は Choice.Id。showDescription なら説明を右に淡く："Open Url    open url in browser"）。</summary>
    private static System.Windows.Controls.ComboBox ChoiceCombo(string id, string itemsPath, string valuePath, bool showDescription = false)
    {
        var c = new System.Windows.Controls.ComboBox { SelectedValuePath = nameof(Choice.Id), Height = 24 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(c, id);
        c.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(itemsPath));
        c.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, new System.Windows.Data.Binding(valuePath));
        var grid = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
        var c1 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
        c1.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, new GridLength(showDescription ? 110 : 0, showDescription ? GridUnitType.Pixel : GridUnitType.Auto));
        grid.AppendChild(c1);
        grid.AppendChild(new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition)));
        var title = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        title.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Choice.Title)));
        grid.AppendChild(title);
        if (showDescription)
        {
            var desc = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            desc.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Choice.Description)));
            desc.SetValue(System.Windows.Controls.Grid.ColumnProperty, 1);
            desc.SetValue(UIElement.OpacityProperty, 0.75);
            grid.AppendChild(desc);
        }
        c.ItemTemplate = new DataTemplate { VisualTree = grid };
        return c;
    }

    /// <summary>Edit Action：左に種類と欄（Url / Script / Path + Parameters、Wait for Exit、Always Show Output）、右に "Available variables:"。</summary>
    private static FrameworkElement EditCustomActionBody()
    {
        var form = new Form(90) { Margin = new Thickness(0, 8, 12, 8) };
        form.Row("Action:", ChoiceCombo("CustomCommandTypeComboBox", nameof(EditCustomActionDialogViewModel.Types), nameof(EditCustomActionDialogViewModel.Type), showDescription: true));
        form.Row("Url:", Text("UrlTextBox", nameof(EditCustomActionDialogViewModel.Url)), visibleWhen: nameof(EditCustomActionDialogViewModel.IsUrl));
        form.Row("Script:", MultiLine("ShScriptTextBox", nameof(EditCustomActionDialogViewModel.Script), 96).With(t => t.FontFamily = new System.Windows.Media.FontFamily("Consolas")), 104, visibleWhen: nameof(EditCustomActionDialogViewModel.IsSh));
        var path = Text("ScriptPathTextBox", nameof(EditCustomActionDialogViewModel.Path));
        var browse = new System.Windows.Controls.Button { Content = "...", Width = 28, Height = 24, Margin = new Thickness(4, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(browse, "ScriptPathButton");
        browse.SetResourceReference(FrameworkElement.StyleProperty, "Gk.Button");
        browse.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*" };
            if (d.ShowDialog() == true && browse.DataContext is EditCustomActionDialogViewModel vm) vm.Path = d.FileName;
        };
        var pathDock = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(browse, System.Windows.Controls.Dock.Right);
        pathDock.Children.Add(browse);
        pathDock.Children.Add(path);
        form.Row("Path:", pathDock, visibleWhen: nameof(EditCustomActionDialogViewModel.IsProcess));
        form.Row("Parameters:", Text("ArgumentsTextBox", nameof(EditCustomActionDialogViewModel.Arguments)), visibleWhen: nameof(EditCustomActionDialogViewModel.IsProcess));
        form.Row(null, Check("WaitForExitCheckBox", "Wait for Exit", nameof(EditCustomActionDialogViewModel.WaitForExit)), 24, visibleWhen: nameof(EditCustomActionDialogViewModel.HasRunOptions));
        form.Row(null, Check("ShowOutputCheckBox", "Always Show Output", nameof(EditCustomActionDialogViewModel.ShowOutput)), 24, visibleWhen: nameof(EditCustomActionDialogViewModel.HasRunOptions));

        var grid = new System.Windows.Controls.Grid { MinHeight = 148 };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(274) });
        grid.Children.Add(form);
        var help = CustomCommandsEditorView.Variables();
        System.Windows.Controls.Grid.SetColumn(help, 1);
        grid.Children.Add(help);
        var border = new System.Windows.Controls.Border { BorderThickness = new Thickness(1), Child = grid };
        border.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "Gk.Border");
        border.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Gk.Panel.Background");
        border.Margin = new Thickness(80, 0, 0, 0);
        return border;
    }

    /// <summary>Edit Custom Command UI Controls：左に ControlsListBox と + / −、右に種類と欄、下に説明。</summary>
    private static FrameworkElement EditCustomControlsBody(EditCustomControlsDialogViewModel vm)
    {
        var heading = new System.Windows.Controls.TextBlock { Text = vm.Title, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 10) };

        // 左：一覧（"$1"、題、種類）
        var list = new System.Windows.Controls.ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetAutomationId(list, "ControlsListBox");
        list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Gk.Input.Border");
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(EditCustomControlsDialogViewModel.Items)));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new System.Windows.Data.Binding(nameof(EditCustomControlsDialogViewModel.Selected)));
        var row = new FrameworkElementFactory(typeof(System.Windows.Controls.DockPanel));
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 2, 2, 2));
        var variable = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        variable.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(CustomControlItemViewModel.Variable)));
        variable.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "VariableTextBlock");
        variable.SetValue(FrameworkElement.WidthProperty, 24.0);
        variable.SetValue(UIElement.OpacityProperty, 0.75);
        row.AppendChild(variable);
        var type = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        type.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(CustomControlItemViewModel.TypeText)));
        type.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "ControlTypeTextBlock");
        type.SetValue(System.Windows.Controls.DockPanel.DockProperty, System.Windows.Controls.Dock.Right);
        type.SetValue(UIElement.OpacityProperty, 0.75);
        row.AppendChild(type);
        var title = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        title.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(CustomControlItemViewModel.Title)));
        title.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        row.AppendChild(title);
        list.ItemTemplate = new DataTemplate { VisualTree = row };
        list.ItemContainerStyle = (Style)Application.Current.FindResource("Gk.SelectionRow");

        var add = PreferencesView.SmallButton("Icon.Plus", () => { });
        System.Windows.Automation.AutomationProperties.SetAutomationId(add, "AddCustomCommandUIControlDropDownButton");
        add.Click += (_, _) =>
        {
            var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = add, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            foreach (var (t, text) in new[] { ("textBox", "Add Text Box"), ("dropdown", "Add Branch Selector"), ("checkBox", "Add Check Box") })
            {
                var mi = new System.Windows.Controls.MenuItem { Header = text };
                mi.Click += (_, _) => vm.Add(t);
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        };
        var remove = PreferencesView.SmallButton("Icon.Minus", vm.RemoveSelected);
        System.Windows.Automation.AutomationProperties.SetAutomationId(remove, "RemoveCustomCommandButton");
        remove.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(EditCustomControlsDialogViewModel.HasSelection)));
        var left = new System.Windows.Controls.DockPanel { Width = 220 };
        var bar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { add, remove }, Margin = new Thickness(2, 2, 0, 0) };
        System.Windows.Controls.DockPanel.SetDock(bar, System.Windows.Controls.Dock.Bottom);
        left.Children.Add(bar);
        left.Children.Add(list);

        // 右：選んだコントロールの欄
        var form = new Form(120) { Margin = new Thickness(12, 0, 0, 0) };
        form.SetBinding(FrameworkElement.DataContextProperty, new System.Windows.Data.Binding(nameof(EditCustomControlsDialogViewModel.Selected)));
        form.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(EditCustomControlsDialogViewModel.HasSelection)) { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        // 選択肢はダイアログの VM が持つ（欄の DataContext は選んだコントロール）
        System.Windows.Controls.ComboBox OwnerCombo(string id, string items, string value)
        {
            var c = ChoiceCombo(id, items, value);
            c.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(items) { Source = vm });
            return c;
        }
        form.Row("Control Type:", OwnerCombo("ControlTypeComboBox", nameof(EditCustomControlsDialogViewModel.ControlTypes), nameof(CustomControlItemViewModel.Type)));
        form.Row("Text Box Type:", OwnerCombo("TextBoxTypeComboBox", nameof(EditCustomControlsDialogViewModel.TextBoxTypes), nameof(CustomControlItemViewModel.TextBoxType)), visibleWhen: nameof(CustomControlItemViewModel.IsTextBox));
        form.Row("Title:", Text("TitleTextBox", nameof(CustomControlItemViewModel.Title)));
        form.Row("Text:", Text("GenericTextBox", nameof(CustomControlItemViewModel.Text)), visibleWhen: nameof(CustomControlItemViewModel.IsGeneric));
        form.Row("Placeholder:", Text("GenericPlaceholderTextBox", nameof(CustomControlItemViewModel.Placeholder)), visibleWhen: nameof(CustomControlItemViewModel.IsGeneric));
        form.Row("Dialog Type:", OwnerCombo("DialogTypeComboBox", nameof(EditCustomControlsDialogViewModel.DialogTypes), nameof(CustomControlItemViewModel.DialogType)), visibleWhen: nameof(CustomControlItemViewModel.IsFilePath));
        form.Row("Default Directory:", Text("DefaultDirectoryTextBox", nameof(CustomControlItemViewModel.DefaultDirectory)), visibleWhen: nameof(CustomControlItemViewModel.IsFilePath));
        form.Row("File Name:", Text("FileNameTextBox", nameof(CustomControlItemViewModel.Filename)), visibleWhen: nameof(CustomControlItemViewModel.IsFilePath));
        form.Row("Filter:", Text("DropdownFilterTextBox", nameof(CustomControlItemViewModel.Filter)), visibleWhen: nameof(CustomControlItemViewModel.IsDropdown));
        form.Row("Checked Value:", Text("CheckBoxCheckedValueTextBox", nameof(CustomControlItemViewModel.CheckedValue)), visibleWhen: nameof(CustomControlItemViewModel.IsCheckBox));
        form.Row("Unchecked Value:", Text("CheckBoxUncheckedValueTextBox", nameof(CustomControlItemViewModel.UncheckedValue)), visibleWhen: nameof(CustomControlItemViewModel.IsCheckBox));
        form.Row(null, Check("CheckBoxDefaultValueCheckBox", "Checked by default", nameof(CustomControlItemViewModel.DefaultValue)), 24, visibleWhen: nameof(CustomControlItemViewModel.IsCheckBox));
        var description = new System.Windows.Controls.TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 12, 0, 0), FontSize = 11.5 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(description, "DescriptionTextBlock");
        description.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Gk.Text.Dim");
        description.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(EditCustomControlsDialogViewModel.Selected) + "." + nameof(CustomControlItemViewModel.Description)));
        var right = new System.Windows.Controls.StackPanel { Children = { form, description } };

        var body = new System.Windows.Controls.DockPanel { Height = 380 };
        System.Windows.Controls.DockPanel.SetDock(left, System.Windows.Controls.Dock.Left);
        body.Children.Add(left);
        body.Children.Add(new System.Windows.Controls.ScrollViewer { Content = right, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto });
        return new System.Windows.Controls.StackPanel { Children = { heading, body } };
    }

    private static FrameworkElement CustomActionResultBody(string output)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Text = output, IsReadOnly = true, Height = 207, FontSize = 12.5,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.SetResourceReference(FrameworkElement.StyleProperty, "Gk.TextBox");
        box.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty, "Gk.CodeFont");
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, "OutputTextBox");
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(80, 0, 0, 0) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Details:", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(box);
        return panel;
    }

    private static DataTemplate BranchItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(System.Windows.Controls.StackPanel));
        panel.SetValue(System.Windows.Controls.StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);
        var icon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        icon.SetValue(System.Windows.Shapes.Path.DataProperty, Icon("Icon.Branch"));
        icon.SetResourceReference(FrameworkElement.StyleProperty, "Gk.IconPath");
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
        panel.AppendChild(icon);
        var text = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        text.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding());
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    /// <summary>File Path のコントロール：dialogType に合わせたシステムのダイアログ。</summary>
    private static string? BrowsePath(CustomCommandControlViewModel c)
    {
        var current = c.Text;
        var dir = System.IO.Directory.Exists(current) ? current : System.IO.Path.GetDirectoryName(current);
        var file = System.IO.Directory.Exists(current) ? "" : System.IO.Path.GetFileName(current);
        switch (c.Control.DialogType)
        {
            case "openDirectory":
            {
                var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = dir ?? "" };
                return d.ShowDialog() == true ? d.FolderName : null;
            }
            case "openFile":
            {
                var d = new Microsoft.Win32.OpenFileDialog { InitialDirectory = dir ?? "", FileName = file };
                return d.ShowDialog() == true ? d.FileName : null;
            }
            default:
            {
                var d = new Microsoft.Win32.SaveFileDialog { InitialDirectory = dir ?? "", FileName = file };
                return d.ShowDialog() == true ? d.FileName : null;
            }
        }
    }

    /// <summary>Update Available のリリースノート（Markdown）。位置と大きさは OutputEditor と同じ。</summary>
    private static FrameworkElement ReleaseNotes(string markdown)
    {
        var view = MarkdownView.Create(markdown, "ReleaseNotesViewer");
        view.Margin = new Thickness(80, 0, 0, 0);
        return view;
    }

    /// <summary>git の出力（読み取り専用のエディタ）。</summary>
    private static FrameworkElement OutputEditor(string text)
    {
        var e = new ICSharpCode.AvalonEdit.TextEditor
        {
            Text = text, IsReadOnly = true, Height = 187, WordWrap = true, FontSize = 12.5, Padding = new Thickness(4),
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
        };
        e.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty, "Gk.CodeFont");
        e.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Gk.Input.Background");
        e.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Text");
        e.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Gk.Input.Border");
        e.BorderThickness = new Thickness(1);
        System.Windows.Automation.AutomationProperties.SetAutomationId(e, "MessageEditor");
        e.TextArea.TextView.LineTransformers.Add(new GitOutputColorizer());
        e.Margin = new Thickness(80, 0, 0, 0);   // 題名の左端から（Git Error / Merge Conflict）
        return e;
    }

    /// <summary>"Local changes:" の 3 択（縦に並べる。Checkout Commit）。</summary>
    private static FrameworkElement LocalChangesRadios()
    {
        var p = new System.Windows.Controls.StackPanel();
        foreach (var r in new[]
        {
            Radio("DoNotChangeRadioButton", "Don't change", "LocalChanges", "LocalChanges", LocalChangesMode.DontChange),
            Radio("StashAndReapplyRadioButton", "Stash and reapply", "LocalChanges", "LocalChanges", LocalChangesMode.StashAndReapply),
            Radio("DiscardRadioButton", "Discard", "LocalChanges", "LocalChanges", LocalChangesMode.Discard),
        })
        {
            // 17px の行を 23px おき
            r.Margin = new Thickness(0, 3, 0, 3);
            r.MinHeight = 17;
            r.HorizontalAlignment = HorizontalAlignment.Left;
            p.Children.Add(r);
        }
        return p;
    }

    private static System.Windows.Controls.Button SideButton(string id, string text)
    {
        var b = new System.Windows.Controls.Button { Content = text, Height = 24, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(b, id);
        return b;
    }

    private static FrameworkElement WithSideButton(FrameworkElement main, FrameworkElement button)
    {
        var dock = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(button, System.Windows.Controls.Dock.Right);
        dock.Children.Add(button);
        dock.Children.Add(main);
        return dock;
    }

    /// <summary>Open Repository の場所と New Host...（足したらそのホストを選ぶ）。</summary>
    private static FrameworkElement LocationRow(OpenRepositoryDialogViewModel vm)
    {
        var add = SideButton("NewHostButton", "New Host...");
        add.Click += (_, _) =>
        {
            var host = vm.CreateAddHost();
            if (DialogWindow.Run(host, For(host)) && host.Added is { } s) vm.AddHost(s);
        };
        return WithSideButton(Combo("LocationComboBox", "Locations", "SelectedLocation"), add);
    }

    /// <summary>Open Repository のパス。This PC だけ Browse...（システムのフォルダ選択。選んだらそのまま開く）を出す。</summary>
    private static FrameworkElement RepositoryPathRow(OpenRepositoryDialogViewModel vm)
    {
        var browse = SideButton("BrowseButton", "Browse...");
        browse.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("IsLocal") { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select repository", InitialDirectory = vm.BrowseStart };
            var window = Window.GetWindow(browse);
            if (dlg.ShowDialog(window) != true) return;
            vm.RepositoryPath = dlg.FolderName;
            (window as DialogWindow)?.Submit();
        };
        return WithSideButton(Text("RemotePathTextBox", "RepositoryPath").Hint("Folder of the repository"), browse);
    }

    /// <summary>New SSH Host の Test Connection（結果はフッター）。</summary>
    private static FrameworkElement TestHostButton(AddSshHostDialogViewModel vm)
    {
        var test = SideButton("TestSshHostButton", "Test Connection");
        test.Margin = new Thickness(0);
        test.HorizontalAlignment = HorizontalAlignment.Left;
        test.Click += async (_, _) => await vm.TestAsync();
        return test;
    }

    /// <summary>Open Repository（WSL と SSH）：パスの下のフォルダの一覧（選ぶとそのフォルダに入る。先頭の ".." で上へ）。</summary>
    private static FrameworkElement RemoteFolders()
    {
        var list = new System.Windows.Controls.ListBox { Height = 160 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(list, "RemoteFoldersListBox");
        list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Gk.Input.Border");
        list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Text");
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("Folders"));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new System.Windows.Data.Binding("SelectedFolder") { Mode = System.Windows.Data.BindingMode.TwoWay });
        list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel Orientation="Horizontal">
                    <Path Data="{DynamicResource Icon.Folder}" Style="{DynamicResource Gk.IconPath}" Width="16" Height="16" Margin="4,0,6,0"/>
                    <TextBlock Text="{Binding}" VerticalAlignment="Center"/>
                </StackPanel>
            </DataTemplate>
            """);
        return list;
    }

    /// <summary>Save stash（一部のファイル）：チェックボックスの一覧と、薄い文字の入ったメッセージ欄。</summary>
    private static FrameworkElement PartialStashBody()
    {
        var list = new System.Windows.Controls.ListBox { Height = 187, Margin = new Thickness(80, 0, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(list, "PartialStashListBox");
        list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Gk.Input.Border");
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("Files"));
        list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Text");
        list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel Orientation="Horizontal">
                    <CheckBox IsChecked="{Binding IsChecked}" Margin="8,0,6,0" VerticalAlignment="Center"/>
                    <Path Data="{DynamicResource Icon.File}" Style="{DynamicResource Gk.IconPath}" Width="16" Height="16" Margin="0,0,6,0"/>
                    <TextBlock Text="{Binding Path}" VerticalAlignment="Center"/>
                </StackPanel>
            </DataTemplate>
            """);
        var message = Text("StashMessageTextBox", "Message").Hint("Stash message (optional)", "Icon.Stash");
        message.Margin = new Thickness(80, 0, 0, 0);
        return new System.Windows.Controls.StackPanel { Children = { list, message } };
    }

    /// <summary>Add Pattern to .gitignore の本文（Pattern: と "(one pattern per line)"、Preview: と "N file matches"）。</summary>
    private static FrameworkElement IgnorePatternBody()
    {
        System.Windows.Controls.Grid Label(string left, string? rightPath, string? rightText, string id)
        {
            var g = new System.Windows.Controls.Grid { Margin = new Thickness(80, 0, 0, 4) };
            g.Children.Add(new System.Windows.Controls.TextBlock { Text = left });
            var r = new System.Windows.Controls.TextBlock { HorizontalAlignment = HorizontalAlignment.Right, FontSize = 11.5, Text = rightText ?? "" };
            if (rightPath is not null) r.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding(rightPath));
            System.Windows.Automation.AutomationProperties.SetAutomationId(r, id);
            g.Children.Add(r);
            return g;
        }
        var pattern = MultiLine("PatternTextBox", "Pattern", 70);
        pattern.Margin = new Thickness(80, 0, 0, 10);
        pattern.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty, "Gk.CodeFont");
        var preview = MultiLine("PreviewTextBox", "Preview", 140);
        preview.IsReadOnly = true;
        preview.Margin = new Thickness(80, 0, 0, 0);
        preview.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty, "Gk.CodeFont");
        return new System.Windows.Controls.StackPanel { Children =
        {
            Label("Pattern:", null, "(one pattern per line)", "PatternLabelTextBlock"), pattern,
            Label("Preview:", "PreviewLabel", null, "PreviewLabelTextBlock"), preview,
        } };
    }

    /// <summary>Add Remote / Edit Remote の本文（Remote: / Repository Url: / Sync submodule URL（追加のときだけ）/ Test Connection）。</summary>
    private static FrameworkElement RemoteBody(RemoteEditDialogViewModel vm)
    {
        var test = new System.Windows.Controls.Button { Content = "Test Connection", Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
        test.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Gk.FlatButton");
        test.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Accent");
        System.Windows.Automation.AutomationProperties.SetAutomationId(test, "TestButton");
        System.Windows.Automation.AutomationProperties.SetName(test, "Test Connection");
        test.Click += async (_, _) => await vm.TestConnectionAsync();
        var status = new System.Windows.Controls.TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        status.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("ConnectionStatus"));
        status.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Gk.Text.Dim");
        var form = new Form()
            .Row("Remote:", Text("RemoteNameTextBox", "Name").Hint(null, "Icon.Remote"))
            .Row("Repository Url:", ProtocolUrl(Text("RepositoryUrlTextBox", "Url").Hint("Git Repository Url", "Icon.Link")));
        if (vm.IsNew) form.Row(null, Check("SyncCheckBox", "Sync submodule URL with parent repository", "SyncSubmoduleUrl"), 26);
        return form.Row(null, Horizontal(test, status), 26);
    }

    /// <summary>ファイルのパスの入力欄と、右のファイル選択ボタン。</summary>
    private static FrameworkElement FileBrowse(string id, string path, string browseId, string filter)
    {
        var box = Text(id, path);
        var browse = new System.Windows.Controls.Button { Width = 23, Height = 23, Margin = new Thickness(4, 0, 0, 0), Content = new Controls.IconImage { Geometry = Icon("Icon.Folder"), Width = 16, Height = 16 } };
        browse.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Gk.BrowseButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(browse, browseId);
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog() == true) box.Text = dlg.FileName;
        };
        var dock = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(browse, System.Windows.Controls.Dock.Right);
        dock.Children.Add(browse);
        dock.Children.Add(box);
        return dock;
    }

    /// <summary>フォルダを選ぶ欄（Create Worktree / Checkout Branch as Worktree の Location）。右のボタンは AutomationId なし。</summary>
    private static FrameworkElement FolderBrowse(string id, string path)
    {
        var box = Text(id, path);
        var browse = new System.Windows.Controls.Button { Width = 23, Height = 23, Margin = new Thickness(4, 0, 0, 0), Content = new Controls.IconImage { Geometry = Icon("Icon.Folder"), Width = 16, Height = 16 } };
        browse.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Gk.BrowseButton");
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            // 入っているパスの、今ある一番近い親から開く（worktree のフォルダはまだ無いことが多い）
            var dir = box.Text.Trim();
            while (dir.Length > 0 && !System.IO.Directory.Exists(dir)) dir = System.IO.Path.GetDirectoryName(dir) ?? "";
            if (dir.Length > 0) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog() == true) box.Text = dlg.FolderName;
        };
        var dock = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(browse, System.Windows.Controls.Dock.Right);
        dock.Children.Add(browse);
        dock.Children.Add(box);
        return dock;
    }

    /// <summary>Add tracking patterns to Git LFS の本文（Add Pattern to .gitignore と同じ形）。</summary>
    private static FrameworkElement LfsTrackBody()
    {
        var body = (System.Windows.Controls.StackPanel)IgnorePatternBody();
        return body;
    }

    /// <summary>LFS Files Status の本文（Filter と一覧、下に鍵のボタン）。</summary>
    private static FrameworkElement LfsStatusBody()
    {
        var filter = Text("FilterTextBox", "Filter");
        filter.Margin = new Thickness(80, 0, 0, 4);
        var list = new System.Windows.Controls.ListBox { Height = 230, Margin = new Thickness(80, 0, 0, 0) };
        list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Gk.Input.Border");
        list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Text");
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("Files"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(list, "FilesListBox");
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(80, 2, 0, 0) };
        foreach (var name in new[] { "Lock", "Unlock" })
        {
            var b = new System.Windows.Controls.Button { Content = name, Margin = new Thickness(0, 0, 4, 0) };
            b.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Gk.FlatButton");
            b.Click += (_, _) => Alerts.NotImplemented(name);
            buttons.Children.Add(b);
        }
        return new System.Windows.Controls.StackPanel { Children = { filter, list, buttons } };
    }

    /// <summary>Report a Bug の本文：撮った画面の縮小（横に並べる）、題名・説明、付けるもの、送り先（GitHub か Forgejo）。</summary>
    private static FrameworkElement BugReportBody(BugReportDialogViewModel vm)
    {
        var previews = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        // 画像ごとに左上のチェックで添付するかを選ぶ（#8。外した画像は薄くする）
        foreach (var choice in vm.Images)
        {
            var image = new System.Windows.Controls.Image { Source = choice.Preview, Height = 120, Stretch = System.Windows.Media.Stretch.Uniform };
            var border = new System.Windows.Controls.Border { BorderThickness = new Thickness(1), Child = image };
            border.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "Gk.Input.Border");
            var check = new System.Windows.Controls.CheckBox { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5, 5, 0, 0), ToolTip = choice.Image.FileName };
            System.Windows.Automation.AutomationProperties.SetAutomationId(check, "AttachImageCheckBox");
            System.Windows.Automation.AutomationProperties.SetName(check, choice.Image.FileName);
            check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(BugReportImageChoice.IsSelected)) { Source = choice });
            void Dim() => image.Opacity = choice.IsSelected ? 1 : 0.35;
            choice.PropertyChanged += (_, _) => Dim();
            Dim();
            previews.Children.Add(new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 8, 0), Children = { border, check } });
        }
        if (vm.Images.Count == 0) previews.Children.Add(Note("No screenshot"));

        return new Form()
            .Row("Screenshot:", previews, 128)
            .Row("Title:", Text("IssueTitleTextBox", "IssueTitle").Hint("What went wrong?"))
            .Row("Description:", MultiLine("DescriptionTextBox", "Description", 100).Hint("Steps to reproduce, expected and actual behavior (optional)"), 108)
            .Row(null, Horizontal(
                Check("IncludeScreenshotsCheckBox", "Attach screenshot", "IncludeScreenshots").With(c => { c.Margin = new Thickness(0, 0, 16, 0); c.IsEnabled = vm.Images.Count > 0; }),
                Check("IncludeLogCheckBox", "Attach log", "IncludeLog")), 26)
            .Row("Server:", Text("ServerUrlTextBox", "ServerUrl").WithoutAutoFocus().Hint("GitHub or Forgejo URL (empty: save only)", "Icon.Link"))
            .Row("Repository:", Text("RepositoryTextBox", "Repository").WithoutAutoFocus().Hint("owner/repo"))
            // 何をすればよいかが分かる文言にする（#9）
            .Row(null, Note("Sign in to the server in the browser, drag the screenshots from the opened folder, then submit the issue."), 22)
            .Row(null, Note($"A copy is saved to {Core.Reporting.BugReport.DefaultDirectory}"), 22);
    }

    /// <summary>
    /// Workspaces の本文（見出し、枠つきの一覧と下の帯の + / -、題名に出すチェック。一覧は窓の高さに合わせて伸びる）。
    /// 右クリックは Add New Workspace / --- / Rename / Delete...、F2 でも名前の入力欄。
    /// </summary>
    private static FrameworkElement WorkspacesBody(WorkspacesDialogViewModel vm)
    {
        var head = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        head.Children.Add(new System.Windows.Controls.TextBlock { Text = vm.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Height = 20 });
        head.Children.Add(new System.Windows.Controls.TextBlock { Text = vm.Subtitle, Margin = new Thickness(0, 2, 0, 0) });

        var list = new System.Windows.Controls.ListBox { Padding = new Thickness(0, 4, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetAutomationId(list, "WorkspacesListBox");
        System.Windows.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(list, System.Windows.Controls.ScrollBarVisibility.Disabled);
        list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Text");
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("Items"));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new System.Windows.Data.Binding("Selected"));
        list.ItemContainerStyle = (Style)Application.Current.FindResource("Gk.RoundRow");
        // 名前を変えているあいだだけ入力欄（文字に合わせた幅、アクセント色の枠、全体を選んだ状態）
        list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
                <Grid>
                    <TextBlock x:Name=""Label"" Text=""{Binding Name}"" VerticalAlignment=""Center"" TextTrimming=""CharacterEllipsis""/>
                    <TextBox x:Name=""Editor"" Text=""{Binding EditName, UpdateSourceTrigger=PropertyChanged}"" Style=""{DynamicResource Gk.TextBox}""
                             Visibility=""Collapsed"" HorizontalAlignment=""Left"" MinWidth=""20"" Height=""22"" Padding=""0"" Margin=""-3,0,0,0""/>
                </Grid>
                <DataTemplate.Triggers>
                    <DataTrigger Binding=""{Binding IsEditing}"" Value=""True"">
                        <Setter TargetName=""Label"" Property=""Visibility"" Value=""Hidden""/>
                        <Setter TargetName=""Editor"" Property=""Visibility"" Value=""Visible""/>
                    </DataTrigger>
                </DataTemplate.Triggers>
            </DataTemplate>");

        void Rename(WorkspaceItem item)
        {
            vm.BeginRename(item);
            list.ScrollIntoView(item);
            list.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (list.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject row || FindDescendant<System.Windows.Controls.TextBox>(row) is not { } box) return;
                box.Focus();
                box.SelectAll();
            });
        }
        // 消す前に確認する（500 幅、"You can't undo this action"、Delete / Cancel）
        void Delete(WorkspaceItem item)
        {
            if (!vm.CanRemove(item)) return;
            if (Alerts.Confirm($"Do you want to delete the workspace '{item.Name}'?", "You can't undo this action", "Delete", 500)) vm.Remove(item);
        }
        // 足したらすぐ名前の入力欄にする（#3）
        void Add() => Rename(vm.Add());
        void EndEdit(System.Windows.Controls.TextBox box, bool commit)
        {
            if (box.DataContext is not WorkspaceItem { IsEditing: true } item) return;
            if (commit) vm.CommitRename(item); else vm.CancelRename(item);
            list.Focus();
        }

        list.PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox box)
            {
                // Enter / Esc は入力欄のもの（ダイアログの Close に届かせない）
                if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Escape)
                {
                    EndEdit(box, e.Key == System.Windows.Input.Key.Enter);
                    e.Handled = true;
                }
                return;
            }
            if (e.Key == System.Windows.Input.Key.F2 && vm.Selected is { } s)
            {
                Rename(s);
                e.Handled = true;
            }
        };
        list.AddHandler(UIElement.LostKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox box) EndEdit(box, commit: true);
        }));

        // 行のドラッグで並べ替える（#5）。少し動かしてから始め、カーソルの下の行の位置に入れる
        WorkspaceItem? dragItem = null;
        var dragStart = new Point();
        var dragging = false;
        void EndDrag()
        {
            dragItem = null;
            if (!dragging) return;
            dragging = false;
            if (list.IsMouseCaptured) list.ReleaseMouseCapture();
        }
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragItem = e.OriginalSource is FrameworkElement { DataContext: WorkspaceItem { IsEditing: false } item } ? item : null;
            dragStart = e.GetPosition(list);
        };
        list.PreviewMouseMove += (_, e) =>
        {
            if (dragItem is null) return;
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) { EndDrag(); return; }
            var p = e.GetPosition(list);
            if (!dragging)
            {
                if (Math.Abs(p.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                dragging = true;
                list.CaptureMouse();
            }
            for (var i = 0; i < vm.Items.Count; i++)
            {
                if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row || !row.IsVisible) continue;
                var top = row.TranslatePoint(new Point(0, 0), list).Y;
                if (p.Y >= top && p.Y < top + row.ActualHeight)
                {
                    vm.Move(dragItem, i);
                    list.ScrollIntoView(dragItem);
                    break;
                }
            }
            e.Handled = true;
        };
        list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (dragging) e.Handled = true;
            EndDrag();
        };
        list.LostMouseCapture += (_, _) => { if (dragging && !list.IsMouseCaptured) EndDrag(); };

        var menu = new System.Windows.Controls.ContextMenu();
        System.Windows.Controls.MenuItem Item(string header, Action action)
        {
            var mi = new System.Windows.Controls.MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
            return mi;
        }
        Item("Add New Workspace", Add);
        menu.Items.Add(new System.Windows.Controls.Separator());
        var rename = Item("Rename", () => { if (vm.Selected is { } s) Rename(s); });
        var delete = Item("Delete...", () => { if (vm.Selected is { } s) Delete(s); });
        menu.Opened += (_, _) =>
        {
            rename.IsEnabled = vm.Selected is not null;
            delete.IsEnabled = vm.Selected is { } s && vm.CanRemove(s);
        };
        list.ContextMenu = menu;

        System.Windows.Controls.Button Small(string icon, string id, Action action)
        {
            var b = PreferencesView.SmallButton(icon, action);
            System.Windows.Automation.AutomationProperties.SetAutomationId(b, id);
            return b;
        }
        var framed = PreferencesView.FramedList(list,
            Small("Icon.Plus", "AddWorkspaceButton", Add),
            Small("Icon.Minus", "RemoveWorkspaceButton", () => { if (vm.Selected is { } s) Delete(s); }));
        var show = Check("ShowWorkspaceInTitleCheckBox", "Show workspace name in title bar", "ShowInTitle");
        show.Margin = new Thickness(0, 8, 0, 0);

        var dock = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        System.Windows.Controls.DockPanel.SetDock(show, System.Windows.Controls.Dock.Bottom);
        dock.Children.Add(head);
        dock.Children.Add(show);
        dock.Children.Add(framed);
        return dock;
    }

    private static T? FindDescendant<T>(DependencyObject d) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (c is T t) return t;
            if (FindDescendant<T>(c) is { } r) return r;
        }
        return null;
    }

    /// <summary>Clone の本文（Repository Url / Test Connection / Parent Folder + フォルダ選択 / Name）。</summary>
    private static FrameworkElement CloneBody(CloneDialogViewModel vm)
    {
        var test = new System.Windows.Controls.Button { Content = "Test Connection", Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
        test.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Gk.FlatButton");
        test.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Accent");
        System.Windows.Automation.AutomationProperties.SetAutomationId(test, "TestButton");
        System.Windows.Automation.AutomationProperties.SetName(test, "Test Connection");
        test.Click += async (_, _) => await vm.TestConnectionAsync();
        // URL が空なら押せない（灰色）
        test.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(CloneDialogViewModel.HasUrl)));
        var status = new System.Windows.Controls.TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "StatusTextBlock");
        status.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("ConnectionStatus"));
        status.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Gk.Text.Dim");

        var parent = Text("ParentDirectoryTextBox", "ParentFolder");
        var browse = new System.Windows.Controls.Button { Width = 23, Height = 23, Margin = new Thickness(4, 0, 0, 0), Content = new Controls.IconImage { Geometry = Icon("Icon.Folder"), Width = 16, Height = 16 } };
        browse.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Gk.BrowseButton");
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select parent folder" };
            if (System.IO.Directory.Exists(vm.ParentFolder)) dlg.InitialDirectory = vm.ParentFolder;
            if (dlg.ShowDialog() == true) vm.ParentFolder = dlg.FolderName;
        };
        var parentRow = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(browse, System.Windows.Controls.Dock.Right);
        parentRow.Children.Add(browse);
        parentRow.Children.Add(parent);

        return new Form()
            .Row("Repository Url:", ProtocolUrl(Text("RepositoryUrlTextBox", "Url").Hint("Git Repository Url", "Icon.Link")))
            .Row(null, Horizontal(test, status), 26)
            .Row("Parent Folder:", parentRow)
            .Row("Name:", Text("RepositoryNameTextBox", "Name").Hint("Repository name"));
    }

    /// <summary>アイコン付きの文字列の一覧（Delete Branches の "Branches:"）。</summary>
    private static FrameworkElement ItemsList(string path, string icon, double height)
    {
        var list = new System.Windows.Controls.ListBox { Height = height, VerticalAlignment = VerticalAlignment.Top };
        list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Gk.Input.Border");
        list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Text");
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(path));
        list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse($@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <StackPanel Orientation=""Horizontal"">
                    <Path Data=""{{DynamicResource {icon}}}"" Style=""{{DynamicResource Gk.IconPath}}"" Width=""14"" Height=""14"" Margin=""2,0,6,0""/>
                    <TextBlock Text=""{{Binding}}"" VerticalAlignment=""Center""/>
                </StackPanel>
            </DataTemplate>");
        return list;
    }

    /// <summary>Save stash / Save snapshot の説明。チェックボックスの文字の位置から、通常の文字色で、すぐ下に出す。</summary>
    private static FrameworkElement StageNewFilesNote()
    {
        var t = Note("By default stash ignores new files until you stage them");
        t.Margin = new Thickness(20, -6, 0, 0);
        t.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Gk.Text");
        return t;
    }

    /// <summary>Create Patch の "Revisions:"（コミットのアイコン + "3b121ca  Document options"）。</summary>
    private static FrameworkElement RevisionsBody(string title = "Revisions:", string? id = null)
    {
        var label = new System.Windows.Controls.TextBlock { Text = title, Margin = new Thickness(80, 0, 0, 4) };
        if (id is not null) System.Windows.Automation.AutomationProperties.SetAutomationId(label, id);
        var list = new System.Windows.Controls.ListBox { Height = 80, Margin = new Thickness(80, 0, 0, 0) };
        list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Gk.Input.Border");
        list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Gk.Text");
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("Revisions"));
        list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <StackPanel Orientation=""Horizontal"">
                    <Path Data=""{DynamicResource Icon.Commit}"" Style=""{DynamicResource Gk.IconPath}"" Width=""14"" Height=""14"" Margin=""2,0,6,0""/>
                    <TextBlock Text=""{Binding}"" VerticalAlignment=""Center""/>
                </StackPanel>
            </DataTemplate>");
        return new System.Windows.Controls.StackPanel { Children = { label, list } };
    }

    /// <summary>Reset type（色の点 + 名前 + 説明）。</summary>
    private static FrameworkElement ResetCombo()
    {
        var c = Combo("ResetTypeCombobox", "Types", "SelectedType");
        c.ItemTemplate = (DataTemplate)Application.Current.FindResource("Gk.ResetTypeTemplate");
        return c;
    }
    /// <summary>
    /// Merge Option：選んだ項目は「名前　説明」、ドロップダウンの項目は「名前　説明　オプション（右）」。区切りは押せない 1px の項目。
    /// </summary>
    private static System.Windows.Controls.ComboBox MergeOptionCombo()
    {
        var c = Combo("MergeTypeComboBox", "Options", "SelectedOption");
        c.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid MinWidth="300">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="114"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="{Binding Name}"/>
                    <TextBlock Grid.Column="1" Text="{Binding Description}" Foreground="{DynamicResource Gk.Text.Dim}"/>
                    <TextBlock Grid.Column="2" Text="{Binding Flag}" Foreground="{DynamicResource Gk.Text.Dim}" Margin="16,0,0,0"
                               Visibility="{Binding RelativeSource={RelativeSource AncestorType=ComboBoxItem}, Converter={StaticResource Gk.NotEmptyToVisibility}, FallbackValue=Collapsed}"/>
                </Grid>
            </DataTemplate>
            """);
        var style = new Style(typeof(System.Windows.Controls.ComboBoxItem), (Style?)Application.Current.TryFindResource(typeof(System.Windows.Controls.ComboBoxItem)));
        style.Setters.Add(new Setter(UIElement.IsEnabledProperty, new System.Windows.Data.Binding("IsSelectable")));
        var sep = new DataTrigger { Binding = new System.Windows.Data.Binding("IsSeparator"), Value = true };
        sep.Setters.Add(new Setter(FrameworkElement.HeightProperty, 1.0));
        sep.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(0)));
        style.Triggers.Add(sep);
        c.ItemContainerStyle = style;
        return c;
    }
}

/// <summary>git の出力の行の色（error: / fatal: と、Activity Manager の "$ git ..." の行は太字、hint: は緑）。</summary>
internal sealed class GitOutputColorizer : ICSharpCode.AvalonEdit.Rendering.DocumentColorizingTransformer
{
    protected override void ColorizeLine(ICSharpCode.AvalonEdit.Document.DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        if (text.StartsWith("error:", StringComparison.Ordinal) || text.StartsWith("fatal:", StringComparison.Ordinal) || text.StartsWith("$ ", StringComparison.Ordinal))
            ChangeLinePart(line.Offset, line.EndOffset, el => el.TextRunProperties.SetTypeface(new System.Windows.Media.Typeface(
                el.TextRunProperties.Typeface.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)));
        else if (text.StartsWith("hint:", StringComparison.Ordinal) && Application.Current.TryFindResource("Gk.Change.Added") is System.Windows.Media.Brush b)
            ChangeLinePart(line.Offset, line.EndOffset, el => el.TextRunProperties.SetForegroundBrush(b));
    }
}
