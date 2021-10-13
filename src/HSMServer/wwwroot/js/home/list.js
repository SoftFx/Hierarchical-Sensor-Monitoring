function displayList(data) {
    $('[id^="list_"]').css('display', 'none');
    $('[id^="sensorData_"]').css('display', 'none');
    $('#noData').css('display', 'none');
    hideSensorInfoParentBlocks();

    let id = data.node.id;
    if (id.startsWith("sensor_")) {
        let path = id.substring("sensor_".length);
        let dataElement = $('#sensorData_' + path)[0];
        $('#sensorData_' + path).css('display', 'block');
        $('#sensorInfo_parent_' + path).css('display', 'block');
        let parent = dataElement.parentNode;
        parent.style.display = 'block';
        $('#' + path).click();
    } else {
        hideSensorInfoParentBlocks();
        let path = id;
        let listId = '#list_' + path;
        $(listId).css('display', 'block');
        showAllChildAccordionsById(listId);
    }
}

function showAllChildAccordionsById(id) {
    let element = $(id)[0];
    if (element != undefined) {
        let children = element.childNodes;
        console.log(children);
        children.forEach(ch => {
            if (ch.classList.contains('accordion')) {
                ch.style.display = 'block';
            }
        });    
    }
}

function hideSensorInfoParentBlocks() {
    $('[id^="sensorInfo_parent_"]').css("display", "none");
}