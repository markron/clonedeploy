﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Models
{
    [Table("active_multicast_sessions")]
    public class ActiveMulticastSession
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("multicast_session_id", Order = 1)]
        public int Id { get; set; }

        [Column("multicast_name", Order = 2)]
        public string Name { get; set; }

        [Column("multicast_pid", Order = 3)]
        public int Pid { get; set; }

        [Column("multicast_port", Order = 4)]
        public int Port { get; set; }

        [NotMapped]
        public string Image { get; set; }

        
    }
}