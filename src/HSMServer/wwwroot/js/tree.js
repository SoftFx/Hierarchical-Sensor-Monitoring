function initializeTree() {
    $('#jstree').jstree();

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

function displayList(data) {
    $('[id^="list_"]').css("display", "none");

    if (document.getElementById('list_' + data.node.id) == null) {
        $('#list_sensors_header').css('display', 'none');
        $('#noData').css('display', 'block');
    }
    else {
        $('#list_sensors_header').css('display', 'block');
        $('#noData').css('display', 'none');
    }

    $('#list_' + data.node.id).css('display', 'block');
}