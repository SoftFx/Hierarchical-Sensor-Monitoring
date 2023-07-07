using System;
using System.Collections.Generic;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
namespace HSMServer.Core.Journal;

public class JournalService : IJournalService
{
    private readonly IDatabaseCore _database;


    public JournalService(IDatabaseCore database)
    {
        _database = database;
    }

    public void AddJournal(JournalRecordModel record)
    {
        _database.AddJournalValue(record.Key, record.ToJournalEntity());
    }
    
    public void AddJournals(List<JournalRecordModel> records)
    {
        foreach (var journal in records)
            AddJournal(journal);
    }
    
    public void RemoveJournal(Guid id) => _database.RemoveJournalValue(id);
    
    public async IAsyncEnumerable<List<JournalRecordModel>> GetJournalValuesPage(Guid id, DateTime from, DateTime to, RecordType type, int count)
    {
        var pages = _database.GetJournalValuesPage(id, from, to, type, count);

        await foreach (var page in pages)
        {
            var currPage = new List<JournalRecordModel>(1 << 4);
            foreach (var item in page)
            {
                currPage.Add(new JournalRecordModel(JsonSerializer.Deserialize<JournalEntity>(item.Entity), item.Key));
            }
                
            yield return currPage;
        }
    }

    public void AddJournals(BaseSensorModel model, SensorUpdate update)
    {
        foreach (var journal in BuildUpdateJournals(model, update))
            AddJournal(journal);
    }


    private List<JournalRecordModel> BuildUpdateJournals(BaseSensorModel model, SensorUpdate update)
    {
        var journals = new List<JournalRecordModel>(1 << 4);
        
        CheckUpdate(model.State, update.State, "State");
        CheckUpdate(model.Integration, update.Integration, "Integration");
        CheckUpdate(model.EndOfMuting, update.EndOfMutingPeriod, "End of muting");
        CheckUpdate(model.Description, update.Description, "Description");
        CheckUpdate(model.Settings.KeepHistory, update.KeepHistory, "Keep history");
        CheckUpdate(model.Settings.SelfDestroy, update.SelfDestroy, "Self destroy");
        CheckUpdate(model.Settings.TTL, update.TTL, "Time to sensor live");
        
        if (update.DataPolicies?.Count != 0)
            journals.Add(new JournalRecordModel(model.Id, DateTime.UtcNow, "Data policy update"));
        
        return journals;
        
        void CheckUpdate<T, U>(T property, U updatedProperty, string propertyName = null)
        {
            if (updatedProperty is not null && !updatedProperty.Equals(property))
            {
                if (updatedProperty is IJournalValue updatedValue && property is IJournalValue value)
                {
                    var newValue = updatedValue.GetValue();
                    var oldValue = value.GetValue();
                    if (newValue != oldValue)
                        journals.Add(new JournalRecordModel(model.Id, DateTime.UtcNow, $"{propertyName}: {oldValue} -> {newValue}"));
                }
                else journals.Add(new JournalRecordModel(model.Id, DateTime.UtcNow, $"{propertyName}: {property} -> {updatedProperty}"));
            }
        }
    }
    
    
}