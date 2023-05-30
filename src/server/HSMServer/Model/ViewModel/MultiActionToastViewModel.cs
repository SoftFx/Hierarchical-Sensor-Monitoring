using System;
using System.Collections;
using System.Text;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel;

public sealed class MultiActionToastViewModel
{
    private readonly LimitedQueue<string> _folders = new(5);
    private readonly LimitedQueue<string> _products = new(5);
    private readonly LimitedQueue<string> _nodes = new(5);
    private readonly LimitedQueue<string> _sensors = new(10);

    private const string _deletionError = @"{0} {1} cannot be deleted";
    private const string _editingError = @"{0} {1} can't have {2} interval";


    public string ResponseInfo { get; private set; } = string.Empty;
    
    public string ErrorMessage { get; private set; } = string.Empty;

    
    public void AddItem(NodeViewModel item)
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
    
    public void AddItem(FolderModel folder) => _folders.Enqueue(folder?.Name);
    

    public MultiActionToastViewModel BuildDeletedItemsMessage()
    {
        var response = new StringBuilder(1 << 5);

        _products.ToBuilder(response, "Removed products:");
        _nodes.ToBuilder(response, "Removed nodes:", Environment.NewLine);
        _sensors.ToBuilder(response, "Removed sensors:", Environment.NewLine);
        
        ResponseInfo = response.ToString();
        
        return this;
    }

    public MultiActionToastViewModel BuildEditItemsMessage()
    {
        var response = new StringBuilder(1 << 5);

        _folders.ToBuilder(response, "Edited folders");
        _products.ToBuilder(response, "Edited products:");
        _nodes.ToBuilder(response, "Edited nodes:", Environment.NewLine);
        _sensors.ToBuilder(response, "Edited sensors:", Environment.NewLine);
        
        ResponseInfo = response.ToString();
        
        return this;
    }

    public MultiActionToastViewModel AddError(ToastErrorType errorType, string objectType, string objectName, TimeInterval interval = default)
    {
        switch (errorType)
        {
            case ToastErrorType.Deletion:
                ErrorMessage += $"{string.Format(_deletionError, objectName, objectType)}{Environment.NewLine}";
                    break;
            case ToastErrorType.Edit:
                ErrorMessage += $"{string.Format(_editingError, objectName, objectType, interval)}{Environment.NewLine}";
                break;
        }

        return this;
    }
    
    public enum ToastErrorType
    {
        Deletion,
        Edit
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