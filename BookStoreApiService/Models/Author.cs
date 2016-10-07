﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace BookStoreApiService.Models
{
    public class Author : Entity
    {
        /// <summary>
        /// Author full name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Books of the author
        /// </summary>
        public IList<Book> Books { get; set; } = new List<Book>();
    }
}