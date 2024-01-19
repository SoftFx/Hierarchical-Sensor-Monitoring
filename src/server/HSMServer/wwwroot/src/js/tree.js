import { currentPanel, getPlotSourceView, initDropzone, Model} from "./dashboard";
import {convertToGraphData} from "./plotting";
import {BarPLot, BoolPlot, DoublePlot, EnumPlot, IntegerPlot, TimeSpanPlot} from "./plots";

window.NodeType = { Folder: 0, Product: 1, Node: 2, Sensor: 3, Disabled: 4 };

const AjaxPost = {
    type: 'POST',
    cache: false,
    async: true
};

var searchInterval = 1000; // 1 sec
var emptySearch = false;
var prevState = {};

window.initializeTree = function () {
    initDropzone()
    
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
            'data': {
                url: function (node) {
                    if (node.id === '#') {
                        return refreshTree;
                    }

                    return getNode;
                },
                data: function (node) {
                    return {
                        'id': node.id,
                        'searchParameter': $('#search_field').val()
                    }
                }
            }
        },
        "contextmenu": {
            "items": buildContextMenu
        },
        "plugins": ["state", "contextmenu", "themes", "wholerow"],
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
    }).on('refresh.jstree', function (e, data) {
        refreshTreeTimeoutId = setTimeout(updateTreeTimer, interval);

        if (window.hasOwnProperty('updateSelectedNodeDataTimeoutId')) {
            updateSelectedNodeDataTimeoutId = setTimeout(updateSelectedNodeData, interval);
        }

        if (searchRefresh) {
            $(this).jstree(true).get_json('#', { flat: true }).forEach((node) => {
                if (node.state.loaded === true)
                    $(this).jstree('open_node', node.id);
            })

            $(this).show();
            $('#jstreeSpinner').addClass('d-none');
            searchRefresh = false;
        }

        if (jQuery.isEmptyObject(prevState)) {
            let jstreeState = JSON.parse(localStorage.getItem('jstree'));
            jstreeState.state.core.open.forEach((node) => {
                $(this).jstree('open_node', node);
            })

            jstreeState.state.core.selected.forEach((node) => {
                $(this).jstree('open_node', node);
                $(this).jstree('select_node', node);
            })
        }

        if (emptySearch !== undefined && emptySearch === true)
        {
            let selectedIds = $('#jstree').jstree('get_selected');
            if (selectedIds.length > 0)
                $(`#${selectedIds[0]}`)[0].scrollIntoView();

            emptySearch = false;
        }
    }).on('open_node.jstree', function (e, data) {
        collapseButton.reset();
    }).on('dblclick.jstree', function (event) {
        if (!$("#nav_link_Home").hasClass("active")) {
            let node = $(event.target).closest("li");
            redirectToHome(node.attr('id'));
        }
    });

    $("#search_tree").on('click', function () {
        search($('#search_input').val());
    });

    $('#search_input').on('keyup', function (e) {
        if (e.keyCode == 13) {
            search($(this).val());
        }
    }).on('input', function () {
        if ($(this).val() === '') {
            $('#search_field').val($(this).val());
            emptySearch  = true;
            if (!jQuery.isEmptyObject(prevState))
            {
                let jstreeState = JSON.parse(localStorage.getItem('jstree'));
                prevState.core.selected = jstreeState.state.core.selected;
                let currenotSelectedNode = $('#jstree').jstree('get_node', prevState.core.selected[0]);
                $.ajax({
                    url: `${addNodes}`,
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify(currenotSelectedNode.parents.slice(0, -1))
                })
                jstreeState.state.core = prevState.core;
                localStorage.setItem('jstree', JSON.stringify(jstreeState));
                $('#jstree').jstree(true).refresh(true, true);
                prevState = {};
            }
            else 
                $('#jstree').jstree(true).refresh(true);
        }
        else {
            clearTimeout(refreshTreeTimeoutId);
            refreshTreeTimeoutId = setTimeout(() => search($(this).val()), searchInterval);
        }
    });

    function search(value) {
        if (value === '')
            return;

        clearTimeout(refreshTreeTimeoutId);

        if (window.hasOwnProperty('updateSelectedNodeDataTimeoutId')) {
            clearTimeout(updateSelectedNodeDataTimeoutId);
        }

        if (jQuery.isEmptyObject(prevState))
            prevState = $('#jstree').jstree('get_state')

        $('#search_field').val(value);
        $('#jstree').hide().jstree(true).refresh(true);

        searchRefresh = true;
        $('#jstreeSpinner').removeClass('d-none')
    }
}

window.loadEditSensorStatusModal = function (id) {
    $.ajax({
        url: `${editStatusAction}?sensorId=${id}`,
        type: 'GET',
        datatype: 'json',
        async: true,
        success: (viewData) => {
            $('#editSensorStatus_form').replaceWith(viewData);
        }
    }).done(function () {
        $('#editSensorStatus_modal').modal('show');
    });
}


function buildContextMenu(node) {
    var contextMenu = {};

    let curType = getCurrentElementType(node);

    if (curType === NodeType.Disabled)
        return contextMenu;


    let selectedNodes = $('#jstree').jstree(true).get_selected();
    let selectedNodesCount = selectedNodes.length;

    if (selectedNodesCount > 1) {
        contextMenu["RemoveNode"] = {
            "label": `Remove items`,
            "action": _ => {
                const maxNames = 20;

                let selectedNodesNames = [];
                for (let i = 0; i < Math.min(selectedNodesCount, maxNames); ++i) {
                    selectedNodesNames.push($('#jstree').jstree().get_node(selectedNodes[i]).data.jstree.title)
                }

                let nodesNamesString = selectedNodesNames.join(', ');
                if (selectedNodesCount > maxNames) {
                    nodesNamesString += ` and other ${selectedNodesCount - maxNames} items`;
                }

                showConfirmationModal(
                    `Remove items`,
                    `Do you really want to remove ${selectedNodesCount} selected items (${nodesNamesString})?`,
                    () => {
                        $.ajax({
                            url: `${removeNodeAction}`,
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

                            $('#nodeDataPanel').addClass('d-none');
                        });
                    }
                );
            }
        }

        contextMenu["Edit policies"] = {
            "label": `Edit policies`,
            "action": _ => {
                $('#editMultipleInterval_modal').modal('show')
                $('#editMultipleInterval').submit(function () {
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


    let isManager = node.data.jstree.isManager === "True";

    let isFolder = curType === NodeType.Folder;
    let isSensor = curType === NodeType.Sensor;
    let isProduct = curType === NodeType.Product;

    if (isProduct) {
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
    else if (!isFolder) {
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

        if (isFolder || isProduct) {
            contextMenu["Edit"] = {
                "label": `Edit ${getKeyByValue(curType)}`,
                "action": _ => {
                    if (isFolder)
                        window.location.href = `${editFolderAction}?folderId=${node.id}`;

                    if (isProduct)
                        window.location.href = `${editProductAction}?Product=${node.id}`;
                }
            };
        }

        if (!isFolder) {
            contextMenu["RemoveNode"] = {
                "label": `Remove ${getKeyByValue(curType)}`,
                "action": _ => {
                    let type = getKeyByValue(curType);

                    $.when(getFullPathAction(node.id)).done((path) => {
                        showConfirmationModal(
                            `Remove ${type}`,
                            `Do you really want to remove ${path}?`,
                            () => {
                                $.ajax({
                                    url: `${removeNodeAction}`,
                                    type: 'POST',
                                    cache: false,
                                    async: true,
                                    data: JSON.stringify([node.id]),
                                    contentType: "application/json"
                                })
                                .done(() => {
                                    $('#nodeDataPanel').addClass('d-none');

                                    updateTreeTimer();
                                    showToast(`${type} has been removed`);
                                });
                            }
                        );
                    })
                }
            }
        }

        if (isSensor && !(isMutedState === "True")) {
            contextMenu["ChangeStatus"] = {
                "label": `Edit status`,
                "icon": "/dist/edit.svg",
                "action": _ => {
                    loadEditSensorStatusModal(node.id);
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

        if (isManager && !isSensor) {
            var alertsSubmenu = {}

            alertsSubmenu["Export"] = {
                "label": `Export`,
                "icon": "fa-solid fa-upload",
                "action": _ => {
                    if (isFolder)
                        window.location.href = `${exportFolderAlerts}?folderId=${node.id}`;
                    else
                        window.location.href = `${exportAlerts}?selectedId=${node.id}`;
                }
            }

            alertsSubmenu["Import"] = {
                "label": `Import`,
                "icon": "fa-solid fa-download",
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


function isFolder(node) {
    return node.icon.includes("fa-folder");
}

function unmuteRequest(node){
    return $.ajax(`${unmuteAction}?selectedId=${node.id}`, AjaxPost).done(() => {
        updateTreeTimer();

        if (window.hasOwnProperty('updateSelectedNodeData')) {
            updateSelectedNodeData();
        }
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

    if (node.li_attr.class !== undefined && node.li_attr.class.includes("jstree-leaf"))
        return NodeType.Sensor;

    return NodeType.Node;
}

function getKeyByValue(type) {
    return Object.keys(NodeType).find(key => NodeType[key] === type).toLowerCase();
}