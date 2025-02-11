﻿using System.Collections;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using CreationEditor.Environment;
using CreationEditor.WPF.Models.Record;
using CreationEditor.WPF.Models.Record.Browser;
using CreationEditor.WPF.Services.Record;
using DynamicData;
using DynamicData.Binding;
using Elscrux.WPF.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using MutagenLibrary.References.ReferenceCache;
using Noggog.WPF;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
namespace CreationEditor.WPF.ViewModels.Record;

[TemplatePart(Name = "PART_RecordGrid", Type = typeof(DataGrid))]
public abstract class RecordListVM : DisposableUserControl, IRecordListVM {
    public const string RecordReadOnlyListStyle = "RecordReadOnlyListStyle";
    public const string RecordListStyle = "RecordListStyle";
    
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(RecordListVM));
    
    protected readonly IReferenceQuery ReferenceQuery;
    protected readonly IRecordController RecordController;

    public Type Type { get; set; } = null!;
    [Reactive] public IEnumerable Records { get; set; } = null!;
    [Reactive] public IRecordBrowserSettings RecordBrowserSettings { get; set; }

    public bool IsBusy {
        get => (bool) GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }
    
    protected readonly List<DataGridTextColumn> ExtraColumns = new();

    protected RecordListVM(
        IRecordBrowserSettings recordBrowserSettings,
        IReferenceQuery referenceQuery, 
        IRecordController recordController,
        bool isReadOnly) {
        ReferenceQuery = referenceQuery;
        RecordController = recordController;
        RecordBrowserSettings = recordBrowserSettings;
        
        SetResourceReference(StyleProperty, isReadOnly ? RecordReadOnlyListStyle : RecordListStyle);
    }

    public override void OnApplyTemplate() {
        base.OnApplyTemplate();

        var dataGrid = GetTemplateChild("PART_RecordGrid") as DataGrid;
        foreach (var column in ExtraColumns) {
            dataGrid?.Columns.Add(column);
        }
    }
}

public class RecordListVM<TMajorRecord, TMajorRecordGetter> : RecordListVM
    where TMajorRecord : class, IMajorRecord, TMajorRecordGetter
    where TMajorRecordGetter : class, IMajorRecordGetter {
    private readonly IRecordEditorController _recordEditorController;

    public ReferencedRecord<TMajorRecord, TMajorRecordGetter>? SelectedRecord { get; set; }
    public ReactiveCommand<Unit, TMajorRecord> NewRecord { get; }
    public ReactiveCommand<Unit, Unit> EditSelectedRecord { get; }
    public ReactiveCommand<Unit, TMajorRecord?> DuplicateSelectedRecord { get; }
    public ReactiveCommand<Unit, Unit> DeleteSelectedRecord { get; }

    [Reactive] public new IObservableCollection<ReferencedRecord<TMajorRecord, TMajorRecordGetter>> Records { get; set; }

    public RecordListVM(
        IReferenceQuery referenceQuery,
        IRecordBrowserSettings recordBrowserSettings,
        IRecordEditorController recordEditorController,
        IRecordController recordController)
        : base(recordBrowserSettings, referenceQuery, recordController, false) {
        _recordEditorController = recordEditorController;
        Type = typeof(TMajorRecordGetter);
        
        Records = this.WhenAnyValue(x => x.RecordBrowserSettings.LinkCache, x => x.RecordBrowserSettings.SearchTerm)
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Do(_ => IsBusy = true)
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Select(x => {
                return Observable.Create<ReferencedRecord<TMajorRecord, TMajorRecordGetter>>((obs, cancel) => {
                    foreach (var record in x.Item1.PriorityOrder.WinningOverrides<TMajorRecordGetter>()) {
                        if (cancel.IsCancellationRequested) return Task.CompletedTask;

                        //Skip when browser settings don't match
                        if (!RecordBrowserSettings.Filter(record)) continue;

                        var formLinks = ReferenceQuery.GetReferences(record.FormKey, RecordBrowserSettings.LinkCache);
                        var referencedRecord = new ReferencedRecord<TMajorRecord, TMajorRecordGetter>(record, formLinks);

                        obs.OnNext(referencedRecord);
                    }
                    obs.OnCompleted();
                    return Task.CompletedTask;
                });
            })
            .Select(x => x.ToObservableChangeSet())
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Do(_ => IsBusy = false)
            .ObserveOn(RxApp.TaskpoolScheduler)
            .ToObservableCollection(this);

        NewRecord = ReactiveCommand.Create(() => {
            var newRecord = RecordController.CreateRecord<TMajorRecord, TMajorRecordGetter>();
            var referencedRecord = new ReferencedRecord<TMajorRecord, TMajorRecordGetter>(newRecord);
            Application.Current.Dispatcher.Invoke(() => Records.Add(referencedRecord));
            
            _recordEditorController.OpenEditor<TMajorRecord, TMajorRecordGetter>(newRecord);

            return newRecord;
        });
        
        EditSelectedRecord = ReactiveCommand.Create(() => {
            if (SelectedRecord == null) return;

            var newOverride = RecordController.GetOrAddOverride<TMajorRecord, TMajorRecordGetter>(SelectedRecord.Record);
            _recordEditorController.OpenEditor<TMajorRecord, TMajorRecordGetter>(newOverride);
        });
        
        DuplicateSelectedRecord = ReactiveCommand.Create(() => {
            if (SelectedRecord == null) return null;
            
            var newRecord = RecordController.DuplicateRecord<TMajorRecord, TMajorRecordGetter>(SelectedRecord.Record);
            var referencedRecord = new ReferencedRecord<TMajorRecord, TMajorRecordGetter>(newRecord);
            Application.Current.Dispatcher.Invoke(() => Records.Add(referencedRecord));
            return newRecord;
        });
        
        DeleteSelectedRecord = ReactiveCommand.Create(() => {
            if (SelectedRecord == null) return;
            
            Application.Current.Dispatcher.Invoke(() => RecordController.DeleteRecord<TMajorRecord, TMajorRecordGetter>(SelectedRecord.Record));
            Records.Remove(SelectedRecord);
        });

        _recordEditorController.RecordChanged += (sender, e) => {
            if (e.Record is not TMajorRecordGetter record) return;

            var recordFormKey = e.Record.FormKey;
            var listRecord = Records.FirstOrDefault(r => recordFormKey == r.Record.FormKey);
            if (listRecord != null) {
                listRecord.Record = record;
                listRecord.RaisePropertyChanged(nameof(ReferencedRecord<TMajorRecord, TMajorRecordGetter>.Record));
            }
        };
    }
}
