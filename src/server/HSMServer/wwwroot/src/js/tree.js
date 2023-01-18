var isCurrentUserAdmin = false;
var currentUserProducts = [];

var needToActivateListTab = false;

var currentSelectedNodeId = "";


window.initializeUserRights = function(userIsAdmin, userProducts) {
    isCurrentUserAdmin = userIsAdmin;
    currentUserProducts = userProducts.split(' ');
}

window.initializeTree = function() {
    var sortingType = $("input[name='TreeSortType']:checked");

    $('#jstree').jstree({
        "core": {
            "check_callback": true,
        },
        "contextmenu": {
            "items": customMenu
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

                return timeSorting(timeA, timeB);
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

function timeSorting(a, b) {
    return b.diff(a);
}

function customMenu(node) {
    var tree = $("#jstree").jstree(true);

    var items =
    {
        "AccessKeys": {
            "separator_before": false,
            "separator_after": false,
            "label": "Access keys",
            "action": function (obj) {
                showAccessKeysList(node.id, true);
            }
        },
        "CopyPath": {
            "separator_before": false,
            "separator_after": false,
            "label": "Copy path",
            "action": function (obj) {
                $.ajax({
                    type: 'POST',
                    url: getPath + '?Selected=' + node.id,
                    dataType: 'html',
                    contentType: 'application/json',
                    cache: false,
                    async: true
                }).done(function (data) {
                    copyToClipboard(data);
                });
            }
        },
        "Edit": {
            "separator_before": false,
            "separator_after": false,
            "label": "Edit",
            "action": function (obj) {
                if (node.parents.length == 1) {
                    window.location.href = editProduct + "?Product=" + node.id;
                }
                else if (node.children.length == 0) {
                    $("#sensorInfo_link_" + node.id).click();
                }
            }
        },
        //"BlockSensor": {
        //    "separator_before": false,
        //    "separator_after": false,
        //    "label": "Block sensor",
        //    "icon": "fa-solid fa-ban",
        //    "action": function (obj) {
        //        changeSensorBlockedState(node, true);
        //    }
        //},
        //"UnblockSensor": {
        //    "separator_before": false,
        //    "separator_after": false,
        //    "label": "Unblock sensor",
        //    "icon": "fa-solid fa-ban",
        //    "action": function (obj) {
        //        changeSensorBlockedState(node, false);
        //    }
        //},
        "RemoveNode":{
            "separator_before": true,
            "separator_after": true,
            "label": "Remove",
            "action": function (obj) {
                //modal
                $('#modalDeleteLabel').empty();
                $('#modalDeleteLabel').append('Remove confirmation');
                $('#modalDeleteBody').empty();
                $('#modalDeleteBody').append(`Do you really want to remove "${node.text}" ?`);

                var modal = new bootstrap.Modal(document.getElementById('modalDelete'));
                modal.show();

                //modal confirm
                $('#confirmDeleteButton').off('click').on('click', function () {
                    modal.hide();

                    $.ajax({
                        type: 'POST',
                        url: removeNode + '?Selected=' + node.id,
                        dataType: 'html',
                        contentType: 'application/json',
                        cache: false,
                        async: true
                    }).done(function () {
                        updateTreeTimer();
                        showToast(`Node has been removed`);
                    });
                });

                $('#closeDeleteButton').off('click').on('click', function () {
                    modal.hide();
                });
            }
        },
        
        "CleanHistory": {
            "separator_before": false,
            "separator_after": true,
            "label": "Clean history",
            "action": function (obj) {
                //modal
                $('#modalDeleteLabel').empty();
                $('#modalDeleteLabel').append('Clean history confirmation');
                $('#modalDeleteBody').empty();
                $('#modalDeleteBody').append(`Do you really want to clean history for "${node.text}" ?`);

                var modal = new bootstrap.Modal(document.getElementById('modalDelete'));
                modal.show();

                //modal confirm
                $('#confirmDeleteButton').off('click').on('click', function () {
                    modal.hide();

                    $.ajax({
                        type: 'POST',
                        url: clearHistoryNode + '?Selected=' + node.id,
                        dataType: 'html',
                        contentType: 'application/json',
                        cache: false,
                        async: true
                    }).done(function () {
                        updateTreeTimer();
                        showToast(`Node has been cleared`);
                    });
                });

                $('#closeDeleteButton').off('click').on('click', function () {
                    modal.hide();
                });
            }
        },
        "Notifications": {
            "separator_before": false,
            "separator_after": false,
            "label": "Private notifications",
            "submenu": {
                "EnableNotifications": {
                    "separator_before": false,
                    "separator_after": false,
                    "label": "Enable",
                    "icon": "fab fa-telegram",
                    "action": function (obj) {
                        updateSensorsNotifications(enableNotifications, node);
                    }
                },
                "DisableNotifications": {
                    "separator_before": false,
                    "separator_after": false,
                    "label": "Disable",
                    "icon": "fab fa-telegram",
                    "action": function (obj) {
                        updateSensorsNotifications(disableNotifications, node);
                    }
                },
                "IgnoreNotifications": {
                    "separator_before": false,
                    "separator_after": false,
                    "label": "Ignore",
                    "icon": "fa-solid fa-bell-slash",
                    "action": function (obj) {
                        $.ajax({
                            type: 'get',
                            url: ignoreNotifications + '?Selected=' + node.id,
                            datatype: 'html',
                            contenttype: 'application/json',
                            cache: false,
                            success: function (viewData) {
                                $("#ignoreNotificatios_partial").html(viewData);
                            }
                        }).done(function () {
                            $('#ignoreNotifications_modal').modal('show');
                        });
                    }
                },
                "RemoveIgnoreNotifications": {
                    "separator_before": false,
                    "separator_after": false,
                    "label": "Remove ignoring",
                    "icon": "fa-solid fa-bell",
                    "action": function (obj) {
                        updateSensorsNotifications(removeIgnoringNotifications, node);
                    }
                }
            }
        }
    }

    if (node.parents.length != 1) {
        delete items.AccessKeys;

        if (node.children.length != 0) {
            delete items.Edit;
        }
    }

    // TODO : if you remove Block sensor logic, also remove changeSensorBlockedState, hasUserNodeRights, initializeUserRights functions
    //if (!hasUserNodeRights(node) || node.children.length != 0 || node.parents.length == 1) {
    //    delete items.BlockSensor;
    //    delete items.UnblockSensor;
    //}

    //if ($(`#${node.id} span.blockedSensor-span`).length === 0) {
    //    delete items.UnblockSensor;
    //}
    //else {
    //    delete items.BlockSensor;
    //}

    if (document.getElementById(`${node.id}_ignoreNotifications`)) {
        delete items.Notifications.submenu.EnableNotifications;
        delete items.Notifications.submenu.IgnoreNotifications;
    }
    else if (document.getElementById(`${node.id}_notifications`)) {
        let partialNotifications = $(`#${node.id}_partialNotifications`).val();
        if (partialNotifications !== "True") {
            delete items.Notifications.submenu.EnableNotifications;
        }

        delete items.Notifications.submenu.RemoveIgnoreNotifications;
    }
    else {
        delete items.Notifications.submenu.DisableNotifications;
        delete items.Notifications.submenu.IgnoreNotifications;
        delete items.Notifications.submenu.RemoveIgnoreNotifications;
    }
   
    if (isCurrentUserAdmin === "True")
        return items;
    
    if (!hasUserNodeRights(node)){
        delete items.RemoveNode;
        delete items.CleanHistory;
    }
    
    return items;
}

function updateSensorsNotifications(action, node) {
    $.ajax({
        type: 'post',
        url: action + '?Selected=' + node.id,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false
    }).done(function () {
        updateTreeTimer();
    });
}

function changeSensorBlockedState(node, isBlocked) {
    $.ajax({
        type: 'post',
        url: changeSensorState + '?Selected=' + node.id + '&Block=' + isBlocked,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false,
        success: function () {
            updateTreeTimer();
        }
    });
}

function hasUserNodeRights(node) {
    let productId = node.parents.length === 1
        ? node.id
        : node.parents[node.parents.length - 2];

    return isCurrentUserAdmin === "True" || currentUserProducts.includes(productId);
}