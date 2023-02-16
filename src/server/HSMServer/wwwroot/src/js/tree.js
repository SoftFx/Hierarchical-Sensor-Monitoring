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
                nodeA = this.get_node(a);
                nodeB = this.get_node(b);

                format = "DD/MM/YYYY hh:mm:ss";
                timeA = moment(nodeA.data.jstree.time, format);
                timeB = moment(nodeB.data.jstree.time, format);

                return timeB.diff(timeA);
            }
            else {
                a = this.get_node(a).data.jstree.title.toLowerCase();
                b = this.get_node(b).data.jstree.title.toLowerCase();

                return a > b ? 1 : -1;
            }
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

    console.info(node.data.jstree);

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

    if (isManager) {
        if(curType === NodeType.Sensor){
            let isSensorIgnored = node.data.jstree.isSensorIgnored === "True";
            if(!isSensorIgnored){
                contextMenu["Ignore"] = {
                    "label": `Ignore ${getKeyByValue(curType)}`,
                    "separator_after": true,
                    "separator_before": true,
                    "action": _ => ignoreNotificationsRequest(node, TelegramTarget.Groups, 'true')
                }
            }else{
                contextMenu["Ignore"] = {
                    "label": `Enable ${getKeyByValue(curType)}`,
                    "separator_after": true,
                    "separator_before": true,
                    "action": _ => removeIgnoreStateRequest(node)
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
    } else {
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
        } else {
            notificationSubmenu["Groups enable"] = {
                "label": "Enable for groups",
                "icon": "fab fa-telegram",
                "action": _ => enableNotificationsRequest(node, TelegramTarget.Groups),
            }
        }
    }

    contextMenu["Notifications"] = {
        "label": "Notifications",
        "separator_before": true,
        "submenu": notificationSubmenu,
    };

    return contextMenu;
}
//var items =
//{
//    "Ignore": {
//        "separator_before": false,
//        "separator_after": false,
//        "label": `Ignore ${elementType}`,
//        //"icon": "fa-solid fa-ban",
//        "action": function (_) {
//            setIgnoreState(node, true);
//        }
//    },
//    "Notifications": {
//        "separator_before": false,
//        "separator_after": false,
//        "label": "Notifications",
//        "submenu": {
//            "EnableGroupsNotifications": {
//                "separator_before": false,
//                "separator_after": false,
//                "label": "Enable for groups",
//                "icon": "fab fa-telegram",
//                "action": function (obj) {
//                    updateSensorsNotifications(enableNotifications, node, TelegramTarget.Groups);
//                }
//            },
//            "IgnoreGroupsNotifications": {
//                "separator_before": false,
//                "separator_after": false,
//                "label": "Ignore for groups",
//                "icon": "fa-solid fa-bell-slash",
//                "action": function (obj) {
//                    $.ajax({
//                        type: 'get',
//                        url: ignoreNotifications + '?Selected=' + node.id + '&target=' + TelegramTarget.Groups,
//                        datatype: 'html',
//                        contenttype: 'application/json',
//                        cache: false,
//                        success: function (viewData) {
//                            $("#ignoreNotificatios_partial").html(viewData);
//                        }
//                    }).done(function () {
//                        $('#ignoreNotifications_modal').modal('show');
//                    });
//                }
//            },
//            "RemoveGroupsIgnoreNotifications": {
//                "separator_before": false,
//                "separator_after": false,
//                "label": "Remove ignoring for groups",
//                "icon": "fa-solid fa-bell",
//                "action": function (obj) {
//                    updateSensorsNotifications(removeIgnoringNotifications, node, TelegramTarget.Groups);
//                }
//            },
//            "EnableAccountsNotifications": {
//                "separator_before": false,
//                "separator_after": false,
//                "label": "Enable for accounts",
//                "icon": "fab fa-telegram",
//                "action": function (obj) {
//                    updateSensorsNotifications(enableNotifications, node, TelegramTarget.Accounts);
//                }
//            },
//            "IgnoreAccountsNotifications": {
//                "separator_before": false,
//                "separator_after": false,
//                "label": "Ignore for accounts",
//                "icon": "fa-solid fa-bell-slash",
//                "action": function (obj) {
//                    $.ajax({
//                        type: 'get',
//                        url: ignoreNotifications + '?Selected=' + node.id + '&target=' + TelegramTarget.Accounts,
//                        datatype: 'html',
//                        contenttype: 'application/json',
//                        cache: false,
//                        success: function (viewData) {
//                            $("#ignoreNotificatios_partial").html(viewData);
//                        }
//                    }).done(function () {
//                        $('#ignoreNotifications_modal').modal('show');
//                    });
//                }
//            },
//            "RemoveAccountsIgnoreNotifications": {
//                "separator_before": false,
//                "separator_after": false,
//                "label": "Remove ignoring for accounts",
//                "icon": "fa-solid fa-bell",
//                "action": function (obj) {
//                    updateSensorsNotifications(removeIgnoringNotifications, node, TelegramTarget.Accounts);
//                }
//            }
//        }
//    }
//}

//if (node.parents.length != 1) {
//    delete items.AccessKeys;

//    if (node.children.length != 0) {
//        delete items.Edit;
//    }
//}

//// TODO : if you remove Block sensor logic, also remove changeSensorBlockedState, hasUserNodeRights, initializeUserRights functions
////if (!hasUserNodeRights(node) || node.children.length != 0 || node.parents.length == 1) {
////    delete items.BlockSensor;
////    delete items.UnblockSensor;
////}

////if ($(`#${node.id} span.blockedSensor-span`).length === 0) {
////    delete items.UnblockSensor;
////}
////else {
////    delete items.BlockSensor;
////}

//let isGroupsDisabled = document.getElementById(`${node.id}_groupsDisabledNotifications`);
//let isGroupsIgnored = document.getElementById(`${node.id}_groupsIgnoredNotifications`);
//if (isGroupsDisabled) {
//    delete items.Notifications.submenu.IgnoreGroupsNotifications;
//    delete items.Notifications.submenu.RemoveGroupsIgnoreNotifications;
//}
//if (isGroupsIgnored) {
//    delete items.Notifications.submenu.EnableGroupsNotifications;
//    delete items.Notifications.submenu.IgnoreGroupsNotifications;
//}
//if (!isGroupsDisabled && !isGroupsIgnored) {
//    delete items.Notifications.submenu.EnableGroupsNotifications;
//    delete items.Notifications.submenu.RemoveGroupsIgnoreNotifications;
//}

//let isAccountsEnabled = document.getElementById(`${node.id}_accountsNotifications`);
//let isAccountsIgnored = document.getElementById(`${node.id}_accountsIgnoreNotifications`);
//if (isAccountsEnabled) {
//    delete items.Notifications.submenu.EnableAccountsNotifications;
//    delete items.Notifications.submenu.RemoveAccountsIgnoreNotifications;
//}
//if (isAccountsIgnored) {
//    delete items.Notifications.submenu.EnableAccountsNotifications;
//    delete items.Notifications.submenu.IgnoreAccountsNotifications;
//}
//if (!isAccountsEnabled && !isAccountsIgnored) {
//    delete items.Notifications.submenu.IgnoreAccountsNotifications;
//    delete items.Notifications.submenu.RemoveAccountsIgnoreNotifications;
//}

//if (isCurrentUserAdmin === "True")
//    return items;

//if (!hasUserNodeRights(node)) {
//    delete items.RemoveNode;
//    delete items.CleanHistory;
//}

//return items;
//    return contextMenu;
//}

function enableNotificationsRequest(node, target) {
    return $.ajax(`${enableNotificationsAction}?selectedId=${node.id}&target=${target}`, AjaxPost).done(updateTreeTimer);
}

function removeIgnoreStateRequest(node){
    return $.ajax(`${removeIgnoreStateAction}?selectedId=${node.id}`, AjaxPost).done(updateTreeTimer);
}

function ignoreNotificationsRequest(node, target, isOffTimeModal = 'false') {
    return $.ajax(`${ignoreNotificationsAction}?selectedId=${node.id}&target=${target}&isOffTimeModal=${isOffTimeModal}`, {
        cache: false,
        success: (v) => $("#ignoreNotificatios_partial").html(v),
    }).done(() => $('#ignoreNotifications_modal').modal('show'))
}

//function updateSensorsNotifications(action, node, type, option) {
//    return $.ajax(`${action}?selectedId=${node.id}&target=${type}`, AjaxPost).done(updateTreeTimer);
//}

function getFullPathAction(nodeId) {
    return $.ajax(`${getNodePathAction}?selectedId=${nodeId}&isFullPath=true`, AjaxPost);
}

//function setIgnoreState(node, isIgnored) {
//    $.ajax(`${setIgnoreStateAction}?selectedId=${node.id}&isIgnored=${isIgnored}`,
//        {
//            type: 'post',
//            cache: false,
//            success: updateTreeTimer,
//        });
//}

function getCurrentElementType(node) {
    if (node.children.length === 0)
        return NodeType.Sensor;

    if (node.parents.length === 1)
        return NodeType.Product;

    return NodeType.Node;
}

function getKeyByValue(type) {
    return Object.keys(NodeType).find(key => NodeType[key] === type).toLowerCase();
}