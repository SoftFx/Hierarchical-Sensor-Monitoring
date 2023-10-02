window.collapseButton =  {
    treeState: undefined,
    isTreeCollapsed: undefined,
    
    tree: undefined,
    collapseIcon: undefined,
    
    collapse(){
        this.isTreeCollapsed = true;
        this.treeState = this.tree.jstree('get_state');

        $.ajax({
            type: 'put',
            url: `${closeNode}?nodeIds=${this.treeState.core.open}`,
            cache: false
        }).done(function (){
            collapseButton.tree.jstree().unbind('close_node.jstree');
            collapseButton.tree.jstree('close_all');
            collapseButton.tree.jstree().bind('close_node.jstree', function (e, data) {
                $.ajax({
                    type: 'put',
                    url: `${closeNode}?nodeIds=${data.node.id}`,
                    cache: false
                })
            });
        })
        
        this.collapseIcon.removeClass('fa-regular fa-square-minus').addClass('fa-regular fa-square-plus').attr('title', 'Restore tree')
    },
    
    restore(){
        this.isTreeCollapsed = false;
        this.tree.jstree('set_state', this.treeState);
        
        this.collapseIcon.removeClass('fa-regular fa-square-plus').addClass('fa-regular fa-square-minus').attr('title', 'Save and close tree');
    },
    
    collapseOnClick(){
        if (this.collapseIcon === undefined || this.jstree === undefined)
           this.init()

        if (this.isTreeCollapsed)
            this.restore()
        else 
            this.collapse()
    },

    
    init(){
        this.tree = $('#jstree');
        this.collapseIcon = $('#collapseIcon');
    },
}