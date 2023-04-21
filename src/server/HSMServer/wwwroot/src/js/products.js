window.sortTable = function (test, sortColumn, id) {
    var icon = test.querySelector("svg");
    let asc = icon.classList.contains('fa-sort-up');

    var table, rows, switching, i, x, y, shouldSwitch;

    table = document.getElementById(`folderProductsTable_${id}`);
    switching = true;

    while (switching) {
        switching = false;
        rows = table.rows;

        for (i = 1; i < (rows.length - 1); i++) {
            shouldSwitch = false;

            x = rows[i].getElementsByTagName("TD")[sortColumn];
            y = rows[i + 1].getElementsByTagName("TD")[sortColumn];

            let xdate = (new Date(x.getElementsByTagName('span')[0].attributes[1].value)).getTime()
            let ydate = (new Date(y.getElementsByTagName('span')[0].attributes[1].value)).getTime()

            if (asc === true) {
                if (xdate > ydate) {
                    shouldSwitch = true;
                    break;
                }
            }
            else if (xdate < ydate) {
                shouldSwitch = true;
                break;
            }
        }
        if (shouldSwitch) {
            rows[i].parentNode.insertBefore(rows[i + 1], rows[i]);
            switching = true;
        }
    }

    if (asc)
        icon.classList.toggle('fa-sort-down');
    else
        icon.classList.toggle('fa-sort-up');
}