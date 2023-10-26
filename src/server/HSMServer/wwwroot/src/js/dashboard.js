export function getPlotSourceView(id){
    return new Promise(function (resolve, reject) {
        $.ajax({
            type: 'GET',
            url: sourceLink + `?id=${id}`
        }).done(function (data){
            resolve(data);
        })
    })
}
