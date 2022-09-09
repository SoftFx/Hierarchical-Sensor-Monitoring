function initializeTree() {
    $('#jstree').jstree({
        "core": {
            "check_callback": true,
            "themes": {
                "name": "proton",
                'responsive': true
            }
        },
        "contextmenu": {
            "items": customMenu
        },
        "plugins": ["state", "contextmenu", "themes", "wholerow", "sort"],
        "sort": function (a, b) {
            var isTimeSort = $("input[name='TreeSortType']:checked").val() == "1";

            if (isTimeSort) {
                nodeA = this.get_node(a);
                nodeB = this.get_node(b);

                format = "DD/MM/YYYY hh:mm:ss";
                timeA = moment(nodeA.data.jstree.time, format);
                timeB = moment(nodeB.data.jstree.time, format);

                return timeSorting(timeA, timeB);
            }
            else {
                a = this.get_node(a).text.toLowerCase();
                b = this.get_node(b).text.toLowerCase();

                return a > b ? 1 : -1;
            }
        }
    });

    $('#updateTime').empty();
    $('#updateTime').append('Update Time: ' + new Date().toUTCString());

    initializeActivateNodeTree();
}

function initializeActivateNodeTree() {
    $('#jstree').on('activate_node.jstree', function (e, data) {
        selectNodeAjax(data.node.id)
    });
}

function selectNodeAjax(selectedId) {
    $.ajax({
        type: 'post',
        url: selectNode + '?Selected=' + selectedId,
        datatype: 'html',
        contenttype: 'application/json',
        cache: false,
        success: function (viewData) {
            $("#listSensors").html(viewData);
        }
    }).done(function () {
        initialize();

        var selectedAccordionId = '#accordion_' + selectedId;
        if ($(selectedAccordionId).attr('aria-expanded') == 'false')
            $(selectedAccordionId).click();
    });
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
        "CleanHistory": {
            "separator_before": false,
            "separator_after": true,
            "label": "Clean history",
            "action": function (obj) {
                //modal
                $('#modalDeleteLabel').empty();
                $('#modalDeleteLabel').append('Remove node');
                $('#modalDeleteBody').empty();
                $('#modalDeleteBody').append('Do you really want to remove "' + node.text + '" node?');

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
            "label": "Notifications",
            "submenu": {
                "EnableNotifications": {
                    "separator_before": false,
                    "separator_after": false,
                    "label": "Enable notifications",
                    "icon": "fab fa-telegram",
                    "action": function (obj) {
                        updateSensorsNotifications(enableNotifications, node);
                    }
                },
                "DisableNotifications": {
                    "separator_before": false,
                    "separator_after": false,
                    "label": "Disable notifications",
                    "icon": "fab fa-telegram",
                    "action": function (obj) {
                        updateSensorsNotifications(disableNotifications, node);
                    }
                },
                "IgnoreNotifications": {
                    "separator_before": false,
                    "separator_after": false,
                    "label": "Ignore notifications",
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
    }

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