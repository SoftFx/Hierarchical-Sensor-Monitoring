var needToActivateListTab = false;

var currentSelectedNodeId = "";


window.initializeTree = function () {
    var sortingType = $("input[name='TreeSortType']:checked");

    $('#jstree').jstree({
        "core": {
            "check_callback": true,
        },
        "contextmenu": {
            "items": buildContextMenu
        },
        "plugins": ["state", "contextmenu", "themes", "wholerow", "sort"],
        "sort": function (a, b) {
            let isTimeSort = sortingType.val() == "1";

            if (isTimeSort) {
                a = this.get_node(a).data.jstree.time;
                b = this.get_node(b).data.jstree.time;
            }
            else {
                [a, b] = [this.get_node(b).data.jstree.title.toLowerCase(), this.get_node(a).data.jstree.title.toLowerCase()]
            }
            
            return a < b ? 1 : -1;
        }
    }).on("state_ready.jstree", function () {
        selectNodeAjax($(this).jstree('get_selected'));
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

function initializeActivateNodeTree() {
    $('#jstree').on('activate_node.jstree', function (e, data) {
        if (data.node.id != undefined) {
            selectNodeAjax(data.node.id);
        }
    });
}

function selectNodeAjax(selectedId) {
    if (currentSelectedNodeId == selectedId)
        return;

    currentSelectedNodeId = selectedId;

    // Show spinner only if selected tree node contains 20 children (nodes/sensors) or it is sensor (doesn't have children)
    var selectedNode = $('#jstree').jstree().get_node(selectedId);
    if (!selectedNode || selectedNode.children.length > 20 || selectedNode.children.length == 0) {
        $("#nodeDataSpinner").css("display", "block");
        $('#nodeDataPanel').addClass('hidden_element');
    }

    $.ajax({
        type: 'post',
        url: selectNode + '?Selected=' + selectedId,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false,
        success: function (viewData) {
            $("#nodeDataPanel").html(viewData);
        }
    }).done(function () {
        initialize();

        openAccordions('[id^="grid-accordion_"]');
        openAccordions('[id^="list-accordion_"]');

        var selectedAccordionId = '#accordion_' + selectedId;
        if ($(selectedAccordionId).attr('aria-expanded') == 'false')
            $(selectedAccordionId).click();

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
const NodeType = { Product: 0, Node: 1, Sensor: 2 };

const AjaxPost = {
    type: 'POST',
    cache: false,
    async: true
};

function buildContextMenu(node) {
    let curType = getCurrentElementType(node);
    let isManager = node.data.jstree.isManager === "True";

    var contextMenu = {};

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
    else {
        contextMenu["CopyPath"] = {
            "label": "Copy path",
            "separator_after": true,
            "action": _ => $.ajax(`${getNodePathAction}?selectedId=${node.id}`, AjaxPost).done(copyToClipboard),
        };
    }

    let isMutedState = node.data.jstree.isMutedState;

    if (isManager) {
        if (isMutedState !== '') {
            if (!(isMutedState === "True")) {
                contextMenu["Mute"] = {
                    "label": `Mute ${getKeyByValue(curType)}`,
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
        
        if (curType !== NodeType.Node) {
            contextMenu["Edit"] = {
                "label": `Edit ${getKeyByValue(curType)}`,
                "action": _ => {
                    if (curType === NodeType.Product)
                        window.location.href = `${editProductAction}?Product=${node.id}`;

                    if (curType === NodeType.Sensor)
                        $(`#sensorInfo_link_${node.id}`).click();
                }
            };
        }

        contextMenu["RemoveNode"] = {
            "label": `Remove ${getKeyByValue(curType)}`,
            "action": _ => {
                var modal = new bootstrap.Modal(document.getElementById('modalDelete'));

                //modal
                $('#modalDeleteLabel').empty();
                $('#modalDeleteLabel').append(`Remove ${getKeyByValue(curType)}`);
                $('#modalDeleteBody').empty();

                $.when(getFullPathAction(node.id)).done((path) => {
                    $('#modalDeleteBody').append(`Do you really want to remove ${path} ?`);
                    modal.show();
                })

                //modal confirm
                $('#confirmDeleteButton').off('click').on('click', () => {
                    modal.hide();

                    $.ajax(`${removeNodeAction}?selectedId=${node.id}`, AjaxPost)
                        .done(() => {
                            updateTreeTimer();
                            showToast(`${getKeyByValue(curType)} has been removed`);

                            $(`#${node.parents[0]}_anchor`).trigger('click');
                        });
                });

                $('#closeDeleteButton').off('click').on('click', () => modal.hide());
            }
        }

        contextMenu["CleanHistory"] = {
            "label": "Clean history",
            "action": _ => {
                var modal = new bootstrap.Modal(document.getElementById('modalDelete'));
                //modal
                $('#modalDeleteLabel').empty();
                $('#modalDeleteLabel').append(`Clean history for ${getKeyByValue(curType)}`);
                $('#modalDeleteBody').empty();

                $.when(getFullPathAction(node.id)).done((path) => {
                    $('#modalDeleteBody').append(`Do you really want to clean history for ${path} ?`);
                    modal.show();
                })

                //modal confirm
                $('#confirmDeleteButton').off('click').on('click', () => {
                    modal.hide();

                    $.ajax(`${clearHistoryAction}?selectedId=${node.id}`, AjaxPost)
                        .done(() => {
                            updateTreeTimer();
                            showToast(`${getKeyByValue(curType)} has been cleared`);

                            $(`#${node.parents[0]}_anchor`).trigger('click');
                        });
                });

                $('#closeDeleteButton').off('click').on('click', () => modal.hide());
            }
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
            "label": "Enable for accounts",
            "icon": "fab fa-telegram",
            "action": _ => enableNotificationsRequest(node, TelegramTarget.Accounts),
        }
    }

    if (isManager) {
        isGroupEnabled = node.data.jstree.isGroupsEnable === "True";

        if (isGroupEnabled) {
            notificationSubmenu["Groups ignore"] = {
                "label": "Ignore for groups...",
                "icon": "fab fa-telegram",
                "action": _ => ignoreNotificationsRequest(node, TelegramTarget.Groups),
            }
        }
        else {
            notificationSubmenu["Groups enable"] = {
                "label": "Enable for groups",
                "icon": "fab fa-telegram",
                "action": _ => enableNotificationsRequest(node, TelegramTarget.Groups),
            }
        }
    }
    
    if (!(isMutedState === "True"))
        contextMenu["Notifications"] = {
            "label": "Notifications",
            "separator_before": true,
            "submenu": notificationSubmenu,
        };

    return contextMenu;
}

function enableNotificationsRequest(node, target) {
    return $.ajax(`${enableNotificationsAction}?selectedId=${node.id}&target=${target}`, AjaxPost).done(updateTreeTimer);
}

function unmuteRequest(node){
    return $.ajax(`${unmuteAction}?selectedId=${node.id}`, AjaxPost).done(() => { 
        updateSelectedNodeData();
        updateTreeTimer();
    });
}

function ignoreNotificationsRequest(node, target, isOffTimeModal = 'false') {
    return $.ajax(`${ignoreNotificationsAction}?selectedId=${node.id}&target=${target}&isOffTimeModal=${isOffTimeModal}`, {
        cache: false,
        success: (v) => $("#ignoreNotificatios_partial").html(v),
    }).done(() => $('#ignoreNotifications_modal').modal('show'))
}

function getFullPathAction(nodeId) {
    return $.ajax(`${getNodePathAction}?selectedId=${nodeId}&isFullPath=true`, AjaxPost);
}

function getCurrentElementType(node) {
    if (node.parents.length === 1)
        return NodeType.Product;
    
    if (node.children.length === 0)
        return NodeType.Sensor;
    
    return NodeType.Node;
}

function getKeyByValue(type) {
    return Object.keys(NodeType).find(key => NodeType[key] === type).toLowerCase();
}