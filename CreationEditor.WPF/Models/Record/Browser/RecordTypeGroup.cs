﻿using System.Collections.ObjectModel;
namespace CreationEditor.WPF.Models.Record.Browser; 

public class RecordTypeGroup {
    public string GroupName { get; }
    public ObservableCollection<RecordTypeListing> RecordTypes { get; set; }

    public RecordTypeGroup(string groupName, ObservableCollection<RecordTypeListing> recordTypes) {
        GroupName = groupName;
        RecordTypes = recordTypes;
    }
}
