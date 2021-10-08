function displayList(data) {
    //$('[id^="list_"]').css("display", "none");
    $('[id^="sensorData_"]').css("display", "none");
    $('#noData').css('display', 'none');

    //if (document.getElementById('list_' + data.node.id) == null) {
    //    $('#list_sensors_header').css('display', 'none');
    //    $('#noData').css('display', 'block');
    //}
    //else {
    //    $('#list_sensors_header').css('display', 'block');
    //    $('#noData').css('display', 'none');
    //}

    //$('#list_' + data.node.id).css('display', 'block');
    let id = data.node.id;
    if (id.startsWith("sensor_")) {
        let path = id.substring("sensor_".length);
        $('#sensorData_' + path).css('display', 'block');
    }
}