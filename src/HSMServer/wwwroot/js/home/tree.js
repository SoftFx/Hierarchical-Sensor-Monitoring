function initializeTree() {
    $('#jstree').jstree({
        "core": {
            "themes": {
                "name": "proton",
                'responsive' : true
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
                                    //tree.delete_node($node);

                                    $('#list_' + $node.id).remove();
                                    $('#noData').css('display', 'block');
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
        "plugins": ["state", "contextmenu", "themes", "wholerow"]
    });
    //$('#jstree').jstree();

    $('#updateTime').empty();
    $('#updateTime').append('Update Time: ' + new Date().toUTCString());

    initializeClickTree();
}

function initializeClickTree() {
    $('#jstree').on('activate_node.jstree', function (e, data) {
        if (data == undefined || data.node == undefined || data.node.id == undefined)
            return;
        displayList(data);
    });
}