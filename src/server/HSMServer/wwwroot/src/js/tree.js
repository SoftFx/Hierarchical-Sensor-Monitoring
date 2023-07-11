﻿var needToActivateListTab = false;

window.currentSelectedNodeId = "";


window.initializeTree = function () {
    var sortingType = $("input[name='TreeSortType']:checked");
    
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
                    return { 'id' : node.id };
                }
            }
        },
        "contextmenu": {
            "items": buildContextMenu
        },
        "plugins": ["state", "contextmenu", "themes", "wholerow"],
    }).on("state_ready.jstree", function () {
        selectNodeAjax($(this).jstree('get_selected')[0]);
    }).on('close_node.jstree', function (e, data) {
        $.ajax({
            type: 'put',
            url: `${closeNode}?nodeId=${data.node.id}`,
            cache: false
        })
    }).on('refresh.jstree', function (e, data){
        refreshTreeTimeoutId = setTimeout(updateTreeTimer, interval);
        updateSelectedNodeDataTimeoutId = setTimeout(updateSelectedNodeData, interval);
    });

    initializeActivateNodeTree();
}

window.activateNode = function (currentNodeId, nodeIdToActivate) {
    needToActivateListTab = $(`#list_${currentNodeId}`).hasClass('active');

    $('#jstree').jstree('activate_node', nodeIdToActivate);

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

const TelegramTarget = { Groups: 0, Accounts: 1 };
const NodeType = { Folder: 0, Product: 1, Node: 2, Sensor: 3, Disabled: 4 };

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
                    "action": _ => ignoreNotificationsRequest(node, TelegramTarget.Groups, 'true')
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

                    //modal
                    $('#modalDeleteLabel').empty();
                    $('#modalDeleteLabel').append(`Remove ${getKeyByValue(curType)}`);
                    $('#modalDeleteBody').empty();

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
                                updateTreeTimer();
                                showToast(`${getKeyByValue(curType)} has been removed`);

                                $(`#${node.parents[0]}_anchor`).trigger('click');
                            });
                    });

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
                "label": `Disabe Grafana`,
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
    }

    notificationSubmenu = {}
    isAccEnabled = node.data.jstree.isAccountsEnable === "True";

    if (isAccEnabled) {
        notificationSubmenu["Accounts ignore"] = {
            "label": "Ignore for accounts...",
            "icon": "fab fa-telegram",
            "action": _ => ignoreNotificationsRequest(node, TelegramTarget.Accounts),
        }
    }
    else {
        notificationSubmenu["Accounts enable"] = {
            "label": "Enable for accounts...",
            "icon": "fab fa-telegram",
            "action": _ => enableNotificationsRequest(node, TelegramTarget.Accounts),
        }
    }

    if (isManager) {
        let groups = node.data.jstree.groups;

        for (let chatId in groups) {
            let group = groups[chatId].Name;

            if (groups[chatId].IsEnabled && !groups[chatId].IsIgnored) {
                notificationSubmenu[`Groups ignore ${chatId}`] = {
                    "label": `Ignore for '${group}'...`,
                    "icon": "fab fa-telegram",
                    "action": _ => ignoreNotificationsRequest(node, TelegramTarget.Groups, false, chatId),
                }
            }
            else {
                notificationSubmenu[`Groups enable ${chatId}`] = {
                    "label": `Enable for '${group}'...`,
                    "icon": "fab fa-telegram",
                    "action": _ => enableNotificationsRequest(node, TelegramTarget.Groups, chatId),
                }
            }
        }
    }

    if ((curType === NodeType.Folder && node.children.length != 0) || (isMutedState !== '' && isMutedState !== undefined))
        contextMenu["Notifications"] = {
            "label": "Notifications",
            "separator_before": true,
            "submenu": notificationSubmenu,
        };
    
    return contextMenu;
}

function enableNotificationsRequest(node, target, chat = null) {
    return $.ajax(`${enableNotificationsAction}?selectedId=${node.id}&target=${target}&chat=${chat}`, AjaxPost).done(updateTreeTimer);
}

function unmuteRequest(node){
    return $.ajax(`${unmuteAction}?selectedId=${node.id}`, AjaxPost).done(() => { 
        updateSelectedNodeData();
        updateTreeTimer();
    });
}

function ignoreNotificationsRequest(node, target, isOffTimeModal = 'false', chat = null) {
    return $.ajax(`${ignoreNotificationsAction}?selectedId=${node.id}&target=${target}&isOffTimeModal=${isOffTimeModal}&chat=${chat}`, {
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