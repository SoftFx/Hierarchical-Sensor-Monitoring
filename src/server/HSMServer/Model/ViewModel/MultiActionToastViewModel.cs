using System;
using System.Collections;
using System.Text;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel;

public sealed class MultiActionToastViewModel
{
    private readonly LimitedQueue<string> _folders = new(5);
    
    private readonly LimitedQueue<string> _products = new(5);
    
    private readonly LimitedQueue<string> _nodes = new(5);
    
    private readonly LimitedQueue<string> _sensors = new(10);


    public string DeletionInfo { get; private set; } = string.Empty;
    
    public string ErrorMessage { get; private set; } = string.Empty;

    
    public void AddDeletedItem(NodeViewModel item)
    {
        if (item.RootProduct.Id == item.Id)
        {
            _products.Enqueue((item as ProductNodeViewModel)?.Name);
            return;
        }

        if (item is SensorNodeViewModel sensorNodeViewModel)
        {
            _sensors.Enqueue(sensorNodeViewModel.FullPath);
            return;
        }
        
        _nodes.Enqueue((item as ProductNodeViewModel)?.FullPath);
    }

    public MultiActionToastViewModel BuildDeletedItemsMessage()
    {
        var response = new StringBuilder(1 << 5);

        _products.ToBuilder(response, "Removed products:");
        _nodes.ToBuilder(response, "Removed nodes:", Environment.NewLine);
        _sensors.ToBuilder(response, "Removed sensors:", Environment.NewLine);
        
        DeletionInfo = response.ToString();
        
        return this;
    }

    public MultiActionToastViewModel AddError(string objectName)
    {
        ErrorMessage += $"Folder {objectName} cannot be deleted{Environment.NewLine}";
        return this;
    }
}

public sealed class LimitedQueue<T> : Queue
{
    private readonly int _limit;
    
    private int _overflowCount = 0;
    
    
    public LimitedQueue(int limit) : base(limit)
    {
        _limit = limit;
    }

    public new void Enqueue(T item)
    {
        if (Count < _limit)
            base.Enqueue(item);
        else 
            _overflowCount++;
    }

    public StringBuilder ToBuilder(StringBuilder builder, string header, string separator = ", ")
    {
        if (Count > 0)
            builder.AppendLine(header)
                   .AppendJoin(separator, ToArray())
                   .AppendLine();
            
        if (_overflowCount > 0)
            builder.AppendLine($"... and other {_overflowCount}");
        
        return builder;
    }
}