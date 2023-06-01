﻿using System;
using System.Collections.Generic;
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

    
    private readonly StringBuilder _errorBuilder = new (1 << 5);
    
    private readonly StringBuilder _responseBuilder = new (1 << 5);
    
    
    public string ErrorMessage => _errorBuilder.ToString();
    
    public string ResponseInfo => _responseBuilder.ToString();

    
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


    public MultiActionToastViewModel BuildResponse(string header)
    {
        _folders.ToBuilder(_responseBuilder, $"{header} folders");
        _products.ToBuilder(_responseBuilder, $"{header} products:");
        _nodes.ToBuilder(_responseBuilder, $"{header} nodes:", Environment.NewLine);
        _sensors.ToBuilder(_responseBuilder, $"{header} sensors:", Environment.NewLine);
        
        return this;
    }

    public void AddError(string errorMessage) => _errorBuilder.AppendLine(errorMessage);
    
    public void AddRemoveFolderError(string name) => AddError($"Folder {name} cannot be deleted");

    public void AddCantChangeIntervalError(string name, string type, string policy, TimeInterval interval) => AddError($"{type} {name} can't have {policy} {interval} interval");
}

internal sealed class LimitedQueue<T> : Queue<T>
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