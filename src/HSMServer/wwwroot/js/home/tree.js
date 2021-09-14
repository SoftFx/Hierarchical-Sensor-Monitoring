function initializeTree() {
    $('#jstree').jstree({ "plugins": ["state"] });
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