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
            "items": function ($node) {
                var tree = $("#jstree").jstree(true);

                return {
                    "Delete": {
                        "separator_before": false,
                        "separator_after": false,
                        "label": "Delete",
                        "action": function (obj) {

                            //modal
                            $('#modalDeleteLabel').empty();
                            $('#modalDeleteLabel').append('Remove node');
                            $('#modalDeleteBody').empty();
                            $('#modalDeleteBody').append('Do you really want to remove "' + $node.text + '" node?');

                            var modal = new bootstrap.Modal(document.getElementById('modalDelete'));
                            modal.show();

                            //modal confirm
                            $('#confirmDeleteButton').on('click', function () {
                                modal.hide();

                                $.ajax({
                                    type: 'POST',
                                    url: removeNode + '?Selected=' + $node.id,
                                    dataType: 'html',
                                    contentType: 'application/json',
                                    cache: false,
                                    async: true
                                }).done(function () {
                                    tree.delete_node($node.id);
                                    //tree.disable_node($node.id);

                                    //$node.children.forEach(function (child_id) {
                                        //tree.disable_node(child_id.id);
                                    //});

                                    $('#list_' + $node.id).remove();
                                    $('#noData').css('display', 'block');

                                    $('[id^="list_"][style*="display: block;"]').each(function (index) {
                                        this.remove();
                                    }); 
                                });                               
                            });

                            $('#closeDeleteButton').on('click', function () {
                                modal.hide();
                            });
                        }
                    }
                }
            }
        },
        "plugins": ["state", "contextmenu", "themes", "wholerow", "sort"],
        "sort": function (a, b) {

            if (isTimeSorting) {
                nodeA = this.get_node(a);
                nodeB = this.get_node(b);

                format = "DD/MM/YYYY hh:mm:ss";
                timeA = moment(nodeA.data.jstree.time, format);
                timeB = moment(nodeB.data.jstree.time, format);

                return timeSorting(timeA, timeB);
            }
            else {
                a = this.get_text(a);
                b = this.get_text(b);

                return nameSorting(a, b);
            }
        }
    });

    $('#updateTime').empty();
    $('#updateTime').append('Update Time: ' + new Date().toUTCString());

    initializeActivateNodeTree();
}

function initializeActivateNodeTree() {
    $('#jstree').on('activate_node.jstree', function (e, data) {
        var selectedId = data.node.id;

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
    });
}

function nameSorting(a, b) {
    return a.toLowerCase() > b.toLowerCase() ? 1 : -1;
}

function timeSorting(a, b) {
    return b.diff(a);
}