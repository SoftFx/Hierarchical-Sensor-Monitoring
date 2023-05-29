using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel;

public class DeletionViewModel
{
    private LimitedQueue<ProductNodeViewModel> DeletedProducts { get; } = new(5);
    
    private LimitedQueue<ProductNodeViewModel> DeletedNodes { get; } = new(5);
    
    private LimitedQueue<SensorNodeViewModel> DeletedSensors { get; } = new(10);
    

    public string DeletionMessage { get; private set; }
    
    public string DeletionErrorMessage { get; private set; }
    
    public void AddDeletedItem(NodeViewModel item)
    {
        if (item.RootProduct.Id == item.Id)
        {
            DeletedProducts.Enqueue(item as ProductNodeViewModel);
            return;
        }

        if (item is SensorNodeViewModel sensorNodeViewModel)
        {
            DeletedSensors.Enqueue(sensorNodeViewModel);

            return;
        }
        
        DeletedNodes.Enqueue(item as ProductNodeViewModel);
    }

    public DeletionViewModel BuildDeletedItemsMessage(bool deleteFolderAttempt)
    {
        var response = new StringBuilder(10);

        if (DeletedProducts.Count > 0)
        {
            response.AppendLine("Removed products:")
                .AppendJoin(", ", DeletedProducts.Select(x => x.Name))
                .AppendLine();
            
            if (DeletedProducts.OverflowCount > 0)
                response.AppendLine($"... and other {DeletedProducts.OverflowCount}");
        }
        
        if (DeletedNodes.Count > 0)
        {
            response.AppendLine("Removed nodes:")
                .AppendJoin("\n", DeletedNodes.Select(x => x.FullPath))
                .AppendLine();
            
            if (DeletedNodes.OverflowCount > 0)
                response.AppendLine($"... and other {DeletedNodes.OverflowCount}");
        }
        
        if (DeletedSensors.Count > 0)
        {
            response.AppendLine("Removed sensors:")
                .AppendJoin("\n", DeletedSensors.Select(x => x.FullPath))
                .AppendLine();
            
            if (DeletedSensors.OverflowCount > 0)
                response.AppendLine($"... and other {DeletedSensors.OverflowCount}");
        }

        if (deleteFolderAttempt)
            DeletionErrorMessage = "Folders cannot be deleted";

        DeletionMessage = response.ToString();
        
        return this;
    }
}

public class LimitedQueue<T> : Queue<T>
{
    private readonly int _limit;

    
    public int OverflowCount = 0;
    
    
    public LimitedQueue(int limit) : base(limit)
    {
        _limit = limit;
    }

    public new void Enqueue(T item)
    {
        while (Count > _limit)
        {
            OverflowCount++;
            Dequeue();
        }
        base.Enqueue(item);
    }
}