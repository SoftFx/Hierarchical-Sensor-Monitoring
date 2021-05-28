// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

function initializeTree() {
        $('#jstree').jstree();

        $('#updateTime').empty();
        $('#updateTime').append('Update Time: ' + new Date().toUTCString());

        $('#jstree').on('activate_node.jstree', function (e, data) {
            if (data == undefined || data.node == undefined || data.node.id == undefined)
                return;
            //alert('clicked node: ' + data.node.id);

            $('[id^="list_"]').css("display", "none"); //start with

            if (document.getElementById('list_' + data.node.id) == null)
                $('#list_sensors_header').css('display', 'none');
            else
                $('#list_sensors_header').css('display', 'block');

            $('#list_' + data.node.id).css('display', 'block');
        });
}



