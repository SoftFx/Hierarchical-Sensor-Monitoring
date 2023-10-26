var needToActivateListTab = false;

window.currentSelectedNodeId = "";

interact('.dropzone').dropzone({
    overlap: 0.75,

    ondropactivate: function (event) {
        event.target.classList.add('drop-active')
    },
    ondragenter: function (event) {
        var draggableElement = event.relatedTarget
        var dropzoneElement = event.target

        dropzoneElement.classList.add('drop-target')
        draggableElement.classList.add('can-drop')
    },
    ondragleave: function (event) {
        event.target.classList.remove('drop-target')
        event.relatedTarget.classList.remove('can-drop')
    },
    ondrop: function (event) {
        // alert(event.relatedTarget.id
        //     + ' was dropped into '
        //     + event.target.id)
        console.log(event.relatedTarget)
        console.log(event.target)
        console.log('On drop event:')
        console.log(event.relatedTarget)
        event.target.innerHTML += event.relatedTarget.innerHTML;
    },
    ondropdeactivate: function (event) {
        event.target.classList.remove('drop-active')
        event.target.classList.remove('drop-target')
    }
})

interact('.drag-drop')
    .draggable({
        inertia: true,
        modifiers: [],
        autoScroll: true,
        listeners: {
            start (event) {
                event.target.style.position = "fixed";
            },
            move: dragMoveListener,
            end: showEventInfo
        }
    })

function showEventInfo (event) {
    console.log('On end:')
    console.log(event)
    event.target.style.transform = '';
    event.target.style.position = 'relative';
    event.target.removeAttribute('data-x')
    event.target.removeAttribute('data-y')
}

function dragMoveListener (event) {
    var target = event.target
    var x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx
    var y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy

    target.style.transform = 'translate(' + x + 'px, ' + y + 'px)'

    target.setAttribute('data-x', x)
    target.setAttribute('data-y', y)
}

window.dragMoveListener = dragMoveListener

window.initializeTree = function () {
    var sortingType = $("input[name='TreeSortType']:checked");
    var searchRefresh = false;
    
    if (window.localStorage.jstree) {
        let initOpened = JSON.parse(window.localStorage.jstree).state.core.open.length;
        if (initOpened > 1)
            isRefreshing = true;
    }
    
    $('#jstree').jstree({
        "core": {
            "check_callback": true,
            "multiple": true,
            'data' : {
                url : function (node) {
                    if (node.id === '#') {
                        return refreshTree;
                    }

                    return getNode;
                },
                data: function (node) {
                    return { 
                        'id' : node.id,
                        'searchParameter': $('#search_field').val()
                    }
                }
            }
        },
        "contextmenu": {
            "items": buildContextMenu
        },
        // "dnd" :{
        //     copy: false,
        //     blank_space_drop: true,
        //     use_html5: true
        // },
        "plugins": ["state", "contextmenu", "themes", "wholerow", 
            //"dnd"
        ],
    }).on("state_ready.jstree", function () {
        selectNodeAjax($(this).jstree('get_selected')[0]);
    }).on('close_node.jstree', function (e, data) {
        if (collapseButton.isTriggered)
            return;

        $.ajax({
            type: 'put',
            url: closeNode,
            cache: false,
            contentType: 'application/json',
            data: JSON.stringify({
                nodeIds: [data.node.id]
            })
        })
    }).on('refresh.jstree', function (e, data){
        refreshTreeTimeoutId = setTimeout(updateTreeTimer, interval);
        updateSelectedNodeDataTimeoutId = setTimeout(updateSelectedNodeData, interval);

        if (searchRefresh) {
            $(this).jstree(true).get_json('#', { flat: true }).forEach((node) => {
                if (node.state.loaded === true)
                    $(this).jstree('open_node', node.id);
            })
            
            $(this).show();
            $('#jstreeSpinner').addClass('d-none');
            searchRefresh = false;
        }
    }).on('open_node.jstree', function (e, data){
        collapseButton.reset();
    });

    $("#search_tree").on('click', function () {
        search($('#search_input').val());
    });

    $(document).on('dnd_start.vakata', function(e, data) {
        console.log('Started dragging node from jstree');
    });
    $(document).on('dnd_move.vakata', function(e, data) {
        console.log('Moving node from jstree to div');
    });

    $(document).on('dnd_stop.vakata', function(e, data) {
        console.log('Stop moving node from jstree to div');
    })
    
    $('#search_input').on('keyup', function (e){
        if (e.keyCode == 13){
            search($(this).val()); 
        }
    }).on('input', function(){
       if ($(this).val() === ''){
           $('#search_field').val($(this).val());
           $('#jstree').jstree(true).refresh(true);
       } 
    });
    
    function search(value){
        if (value === '')
            return;

        clearTimeout(refreshTreeTimeoutId)
        clearTimeout(updateSelectedNodeDataTimeoutId)

        $('#search_field').val(value);
        $('#jstree').hide().jstree(true).refresh(true);

        searchRefresh = true;
        $('#jstreeSpinner').removeClass('d-none')
    }

    initializeActivateNodeTree();
}

window.activateNode = function (currentNodeId, nodeIdToActivate) {
    needToActivateListTab = $(`#list_${currentNodeId}`).hasClass('active');

    $('#jstree').jstree('activate_node', nodeIdToActivate);
    $('#jstree').jstree('open_node', nodeIdToActivate);

    if (currentSelectedNodeId != nodeIdToActivate) {
        selectNodeAjax(nodeIdToActivate);
    }
}

function isDisabled(node) {
    return typeof node.disabled === 'undefined';
}

function isFolder(node) {
    return node.icon.includes("fa-folder");
}

function initializeActivateNodeTree() {
    $('#jstree').on('activate_node.jstree', function (e, data) {
        if (data.node.id != undefined) {
            selectNodeAjax(data.node.id);
        }
    });
}

function selectNodeAjax(selectedId) {
    if (currentSelectedNodeId == selectedId || selectedId == undefined)
        return;

    let isEditMode = !$("#description").hasClass("d-none") && currentSelectedNodeId !== "";

    if (isEditMode) {
        saveMetaData(selectedId);
    }
    else {
        initSelectedNode(selectedId);
    }
}

function saveMetaData(selectedId) {
    let form = document.getElementById("editMetaInfo_form");
    let formData = new FormData(form);
    collectAlerts(formData);

    $.ajax({
        type: 'POST',
        url: isDataValidAction,
        data: formData,
        processData: false,
        contentType: false,
        async: true
    }).done(function (isValid) {
        let isAlertsValid = true;
        $("#editMetaInfo_form").find("div.dataAlertRow").each(function () {
            $(this).find(`input[name='Comment']`).each(function () {
                isAlertsValid &= $(this)[0].checkValidity();
            });

            $(this).find('input[name="Target"]').each(function () {
                isAlertsValid &= $(this)[0].checkValidity();
            });
        });

        if (isValid && isAlertsValid) {
            let path = $("#nodeHeader").text();

            showConfirmationModal(
                `Saving changes`,
                `Do you want to save '${path}' changes?`,
                () => {
                    $.ajax({
                        url: form.action,
                        type: 'POST',
                        data: formData,
                        processData: false,
                        contentType: false,
                        async: true
                    }).done(() => initSelectedNode(selectedId));
                },
                () => initSelectedNode(selectedId),
                "Yes",
                "No"
            );
        }
        else {
            initSelectedNode(selectedId);
        }
    });
}

function initSelectedNode(selectedId) {
    currentSelectedNodeId = selectedId;

    // Show spinner only if selected tree node contains 20 children (nodes/sensors) or it is sensor (doesn't have children)
    var selectedNode = $('#jstree').jstree().get_node(selectedId);
    if (!selectedNode || selectedNode.children.length > 20 || selectedNode.children.length == 0) {
        $("#nodeDataSpinner").css("display", "block");
        $('#nodeDataPanel').addClass('hidden_element');
    }

    $.ajax({
        type: 'post',
        url: `${selectNode}?selectedId=${selectedId}`,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false,
        success: function (viewData) {
            $("#nodeDataPanel").html(viewData);
        }
    }).done(function () {
        initialize();

        if (needToActivateListTab) {
            selectNodeInfoTab("list", selectedId);
            needToActivateListTab = false;
        }
        else {
            selectNodeInfoTab("grid", selectedId);
        }

        $("#nodeDataSpinner").css("display", "none");
        $('#nodeDataPanel').removeClass('hidden_element');
    });
}

function openAccordions(accordionsId) {
    let accordions = document.querySelectorAll(accordionsId);

    accordions.forEach(element => {
        if (element.getAttribute('aria-expanded') == 'false') {
            element.click();
        }
    });
}

function selectNodeInfoTab(tab, selectedId) {
    let tabLink = document.getElementById(`${tab}Link_${selectedId}`);

    if (tabLink != null)
        tabLink.click();
}

window.NodeType = { Folder: 0, Product: 1, Node: 2, Sensor: 3, Disabled: 4 };

const AjaxPost = {
    type: 'POST',
    cache: false,
    async: true
};

function buildContextMenu(node) {
    var contextMenu = {};
    
    let curType = getCurrentElementType(node);
    
    if (curType === NodeType.Disabled)
        return contextMenu;
    
    let isManager = node.data.jstree.isManager === "True";
    
    let selectedNodes = $('#jstree').jstree(true).get_selected();
    
    if (selectedNodes.length > 1) {
        contextMenu["RemoveNode"] = {
            "label": `Remove items`,
            "action": _ => {
                var modal = new bootstrap.Modal(document.getElementById('modalDelete'));

                //modal
                $('#modalDeleteLabel').empty().append(`Remove items`);
                $('#modalDeleteBody').empty().append(`Do you really want to remove ${selectedNodes.length} selected items?`);
                modal.show();
                
                //modal confirm
                $('#confirmDeleteButton').off('click').on('click', () => {
                    modal.hide();
                    
                    $.ajax({
                        url:`${removeNodeAction}`,
                        type: 'POST',
                        cache: false,
                        async: true,
                        data: JSON.stringify(selectedNodes),
                        contentType: "application/json"
                    }).done((response) => {
                        updateTreeTimer();
                        
                        let message = response.responseInfo.replace(/(?:\r\n|\r|\n)/g, '<br>')

                        if (response.errorMessage !== "")
                            message += `<span style="color: red">${response.errorMessage.replace(/(?:\r\n|\r|\n)/g, '<br>')}</span>`
                        
                        showToast(message);

                        $(`#${$('#jstree').jstree(true).get_node('#').children[0]}_anchor`).trigger('click');
                    });
                });

                $('#closeDeleteButton').off('click').on('click', () => modal.hide());
            }
        }
        
        contextMenu["Edit policies"] = {
            "label": `Edit policies`,
            "action": _ => {
                $('#editMultipleInterval_modal').modal('show')
                $('#editMultipleInterval').submit(function() {
                    event.preventDefault();
                    event.stopImmediatePropagation();
                    $('#NodeIds')[0].value = selectedNodes;

                    $.ajax({
                        url: $("#editMultipleInterval").attr("action"),
                        type: 'POST',
                        data: $("#editMultipleInterval").serialize(),
                        datatype: 'json',
                        async: true,
                        success: (response) => {
                            updateTreeTimer();

                            let message = response.responseInfo.replace(/(?:\r\n|\r|\n)/g, '<br>')

                            if (response.errorMessage !== "")
                                message += `<span style="color: red">${response.errorMessage.replace(/(?:\r\n|\r|\n)/g, '<br>')}</span>`

                            showToast(message);

                            hideAlertsModal();
                        },
                        error: function (jqXHR) {
                            $('#editMultipleInterval span.field-validation-valid').each(function () {
                                let errFor = $(this).data('valmsgFor');
                                if (jqXHR.responseJSON[errFor] !== undefined) {
                                    $(this).removeClass('field-validation-valid')
                                    $(this).addClass('field-validation-error')
                                    $(this).html(jqXHR.responseJSON[errFor][0]);
                                }
                            })
                        }
                    });
                });
            }
        }
        return contextMenu;
    }

    if (curType === NodeType.Product) {
        contextMenu["AccessKeys"] = {
            "label": "Access keys",
            "separator_after": true,
            "action": _ => showAccessKeysList(node.id, true),
        };
        
        contextMenu["CopyName"] = {
            "label": "Copy name",
            "separator_after": true,
            "action": _ => copyToClipboard(node.data.jstree.title),
        };
    }
    else if (curType !== NodeType.Folder) {
        contextMenu["CopyPath"] = {
            "label": "Copy path",
            "separator_after": true,
            "action": _ => $.ajax(`${getNodePathAction}?selectedId=${node.id}`, AjaxPost).done(copyToClipboard),
        };
    }

    let isMutedState = node.data.jstree.isMutedState;

    if (isManager) {
        if (isMutedState !== undefined && isMutedState !== '') {
            if (!(isMutedState === "True")) {
                contextMenu["Mute"] = {
                    "label": `Mute ${getKeyByValue(curType)} for...`,
                    "separator_after": true,
                    "separator_before": true,
                    "action": _ => muteRequest(node)
                }
            }
            else {
                contextMenu["Mute"] = {
                    "label": `Unmute ${getKeyByValue(curType)}`,
                    "separator_after": true,
                    "separator_before": true,
                    "action": _ => unmuteRequest(node)
                }
            } 
        }

        if (curType !== NodeType.Node && curType !== NodeType.Sensor) {
            contextMenu["Edit"] = {
                "label": `Edit ${getKeyByValue(curType)}`,
                "action": _ => {
                    if (curType === NodeType.Folder)
                        window.location.href = `${editFolderAction}?folderId=${node.id}`;

                    if (curType === NodeType.Product)
                        window.location.href = `${editProductAction}?Product=${node.id}`;
                }
            };
        }

        if (curType != NodeType.Folder) {
            contextMenu["RemoveNode"] = {
                "label": `Remove ${getKeyByValue(curType)}`,
                "action": _ => {
                    var modal = new bootstrap.Modal(document.getElementById('modalDelete'));
                    let type = getKeyByValue(curType);
                    //modal
                    $('#modalDeleteLabel').empty();
                    $('#modalDeleteLabel').append(`Remove ${type}`);
                    $('#modalDeleteBody').empty();

                    let prevDom = $('#jstree').jstree('get_prev_dom', node.id);
                    let parent = undefined;

                    if (prevDom)
                        parent = prevDom[0].id

                    
                    $.when(getFullPathAction(node.id)).done((path) => {
                        $('#modalDeleteBody').append(`Do you really want to remove ${path}?`);
                        modal.show();
                    })

                    //modal confirm
                    $('#confirmDeleteButton').off('click').on('click', () => {
                        modal.hide();

                        $.ajax({
                                url:`${removeNodeAction}`,
                                type: 'POST',
                                cache: false,
                                async: true,
                                data: JSON.stringify([node.id]),
                                contentType: "application/json"
                            })
                            .done(() => {
                                selectParentAfterRefresh();
                                
                                updateTreeTimer();
                                showToast(`${type} has been removed`);
                            });
                    });
                    
                    function selectParentAfterRefresh(){
                        setTimeout(function (){
                            if (!isRefreshing)
                            {
                                parent = $(`#${parent}_anchor`);
                                if (jQuery.isEmptyObject(parent[0]))
                                    $('#nodeDataPanel').html('');
                                else
                                    parent.trigger('click');
                            }
                            else 
                                selectParentAfterRefresh();
                        }, 50)
                    }

                    $('#closeDeleteButton').off('click').on('click', () => modal.hide());
                }
            }
        }
        
        if (curType === NodeType.Sensor && !(isMutedState === "True")) {
            contextMenu["ChangeStatus"] = {
                "label": `Edit status`,
                "icon": "/dist/edit.svg",
                "action": _ => {
                    loadEditSensorStatusModal();
                }
            }
        }

        let isGrafanaEnabled = node.data.jstree.isGrafanaEnabled === "True";
        if (isGrafanaEnabled) {
            contextMenu["Grafana disable"] = {
                "label": `Disable Grafana`,
                "icon": "/dist/grafana.svg",
                "action": _ => grafanaRequest(node, disableGrafanaAction),
            };
        }
        else {
            contextMenu["Grafana enable"] = {
                "label": `Enable Grafana`,
                "icon": "/dist/grafana.svg",
                "action": _ => grafanaRequest(node, enableGrafanaAction),
            };
        }

        if (isManager && (curType === NodeType.Product || curType === NodeType.Node)) {
            alertsSubmenu = {}

            alertsSubmenu["Export"] = {
                "label": `Export`,
                "icon": "fa-solid fa-download",
                "action": _ => window.location.href = `${exportAlerts}?selectedId=${node.id}`
            }

            alertsSubmenu["Import"] = {
                "label": `Import`,
                "icon": "fa-solid fa-upload",
                "action": _ => {
                    var $input = $('<input type="file" />');

                    $input.on("change", function () {
                        var file = $(this).prop('files')[0];
                        var fileName = file.name;

                        if (file) {
                            var reader = new FileReader();

                            reader.readAsText(file, "UTF-8");

                            reader.onload = function (evt) {
                                var data = {
                                    "NodeId": node.id,
                                    "FileContent": evt.target.result
                                };

                                $.ajax({
                                    type: 'POST',
                                    data: JSON.stringify(data),
                                    contentType: 'application/json',
                                    url: importAlerts
                                }).done((errorMessage) => {
                                    if (errorMessage) {
                                        showToast(errorMessage, "Importing error!");
                                    }
                                    else {
                                        showToast(`Alerts have been successfully imported.`);
                                    }
                                });
                            }

                            reader.onerror = function () {
                                showToast(`There is some errors while reading the file '${fileName}'.`, "Error!");
                            }
                        }
                    });

                    $input.trigger('click');
                }
            }

            contextMenu["Alerts"] = {
                "label": "Alerts",
                "separator_before": true,
                "submenu": alertsSubmenu,
            };
        }
    }
    
    return contextMenu;
}

function unmuteRequest(node){
    return $.ajax(`${unmuteAction}?selectedId=${node.id}`, AjaxPost).done(() => { 
        updateSelectedNodeData();
        updateTreeTimer();
    });
}

function muteRequest(node) {
    return $.ajax(`${muteAction}?selectedId=${node.id}`, {
        cache: false,
        success: (v) => $("#ignoreNotificatios_partial").html(v),
    }).done(() => $('#ignoreNotifications_modal').modal('show'))
}

function grafanaRequest(node, action) {
    return $.ajax(`${action}?selectedId=${node.id}`, AjaxPost).done(updateTreeTimer);
}

function getFullPathAction(nodeId) {
    return $.ajax(`${getNodePathAction}?selectedId=${nodeId}&isFullPath=true`, AjaxPost);
}

function getCurrentElementType(node) {
    if (node.id.includes('disabled'))
        return NodeType.Disabled;
    
    if (node.parents.length === 1 && isFolder(node))
        return NodeType.Folder;

    if ((node.parents.length === 1 && !isFolder(node)) ||
        (node.parents.length === 2 && isFolder($('#jstree').jstree().get_node(node.parents[0]))))
        return NodeType.Product;
    
    if (typeof node.li_attr.class === 'undefined')
        return NodeType.Sensor;
    
    return NodeType.Node;
}

function getKeyByValue(type) {
    return Object.keys(NodeType).find(key => NodeType[key] === type).toLowerCase();
}