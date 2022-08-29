﻿using Loqui;
namespace CreationEditor.GUI.Models.Record; 

public class RecordTypeListing {
    public ILoquiRegistration Registration { get; }
    public string RecordTypeName { get; set; }

    public RecordTypeListing(ILoquiRegistration registration) {
        Registration = registration;
        RecordTypeName = registration.Name;
    }
}
