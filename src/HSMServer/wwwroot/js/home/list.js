function displayList(data) {
    $('[id^="list_"]').css("display", "none");
    $('[id^="sensorData_"]').css("display", "none");
    $('#noData').css('display', 'none');

    let id = data.node.id;
    if (id.startsWith("sensor_")) {
        let path = id.substring("sensor_".length);
        let dataElement = $('#sensorData_' + path)[0];
        $('#sensorData_' + path).css('display', 'block');
        let parent = dataElement.parentNode;
        parent.style.display = 'block';
    } else {
        let path = id;
        let listId = '#list_' + path;
        $(listId).css('display', 'block');
        showAllChildrenById(listId);
    }
}

function showAllChildrenById(id) {
    let element = $(id)[0];
    let children = element.childNodes;
    children.forEach(ch => ch.style.display = 'block');
}