# Generated by Django 3.0.2 on 2020-03-19 19:13

from django.db import migrations, models


class Migration(migrations.Migration):

    dependencies = [
        ('agotboardgame_main', '0009_auto_20200319_1912'),
    ]

    operations = [
        migrations.AlterField(
            model_name='game',
            name='created_at',
            field=models.DateTimeField(auto_now_add=True),
        ),
    ]
