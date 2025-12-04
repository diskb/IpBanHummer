using System;
using System.ComponentModel.DataAnnotations;

namespace Sample.Models
{
    public class SimpleFormModel
    {
        [Range(105, 110)]
        [Display(Name = "Некоторое число. В проде это логин-пароль например")]
        public int Value { get; set; }
    }
}
