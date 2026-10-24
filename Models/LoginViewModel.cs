using System.ComponentModel.DataAnnotations;

namespace TogetherCraft.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Введи логин.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Логин должен содержать от 3 до 32 символов.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Введи пароль.")]
    public string Password { get; set; } = "";
}
